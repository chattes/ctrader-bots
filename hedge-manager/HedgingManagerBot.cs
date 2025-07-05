using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HedgingManagerBot : Robot
    {
        [Parameter("Symbol", DefaultValue = "EURUSD")]
        public string MonitoredSymbol { get; set; }

        [Parameter("Check Interval (seconds)", DefaultValue = 1, MinValue = 1)]
        public int CheckInterval { get; set; }

        [Parameter("Losing Position Trim Percentage", DefaultValue = 0.75, MinValue = 0.1, MaxValue = 0.95)]
        public double LosingPositionTrimPercentage { get; set; }

        [Parameter("Enable Logging", DefaultValue = true)]
        public bool EnableLogging { get; set; }

        [Parameter("Enable Detailed Logging", DefaultValue = false)]
        public bool EnableDetailedLogging { get; set; }

        [Parameter("Max Position Close Retries", DefaultValue = 3, MinValue = 1, MaxValue = 10)]
        public int MaxRetries { get; set; }

        private Timer _monitoringTimer;
        private readonly object _lockObject = new object();
        private DateTime _lastLogTime = DateTime.MinValue;
        private readonly TimeSpan _logInterval = TimeSpan.FromMinutes(1);

        protected override void OnStart()
        {
            if (EnableLogging)
                Print($"HedgingManagerBot started for symbol: {MonitoredSymbol}");

            ValidateParameters();
            StartMonitoring();
        }

        protected override void OnStop()
        {
            if (_monitoringTimer != null)
            {
                _monitoringTimer.Stop();
                _monitoringTimer.Dispose();
            }

            if (EnableLogging)
                Print("HedgingManagerBot stopped.");
        }

        private void ValidateParameters()
        {
            if (string.IsNullOrEmpty(MonitoredSymbol))
                throw new ArgumentException("Monitored symbol cannot be empty");

            if (CheckInterval < 1)
                throw new ArgumentException("Check interval must be at least 1 second");

            if (LosingPositionTrimPercentage < 0.1 || LosingPositionTrimPercentage > 0.95)
                throw new ArgumentException("Losing position trim percentage must be between 0.1 and 0.95");
        }

        private void StartMonitoring()
        {
            _monitoringTimer = Timer.Start(TimeSpan.FromSeconds(CheckInterval), MonitorPositions);
        }

        private async void MonitorPositions()
        {
            lock (_lockObject)
            {
                try
                {
                    var symbolPositions = GetSymbolPositions();
                    
                    if (!symbolPositions.Any())
                    {
                        LogPeriodically("No positions found for monitored symbol");
                        return;
                    }

                    var hedgingInfo = PositionAnalyzer.AnalyzeHedging(symbolPositions);
                    
                    if (!hedgingInfo.IsHedged)
                    {
                        LogPeriodically($"No hedging detected - Long: {hedgingInfo.LongPositions.Count}, Short: {hedgingInfo.ShortPositions.Count}");
                        return;
                    }

                    if (EnableDetailedLogging)
                        Print($"Hedging Analysis: {PositionAnalyzer.GetHedgingSummary(hedgingInfo)}");

                    await CheckForTrimOpportunity(hedgingInfo.LongPositions, hedgingInfo.ShortPositions);
                }
                catch (ArgumentException ex)
                {
                    LogError($"Parameter validation error: {ex.Message}");
                    Stop();
                }
                catch (InvalidOperationException ex)
                {
                    LogError($"Invalid operation: {ex.Message}");
                }
                catch (Exception ex)
                {
                    LogError($"Unexpected error in MonitorPositions: {ex.Message}");
                    if (EnableDetailedLogging)
                        Print($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        private List<Position> GetSymbolPositions()
        {
            return Positions.Where(p => p.SymbolName == MonitoredSymbol).ToList();
        }

        private async Task CheckForTrimOpportunity(List<Position> longPositions, List<Position> shortPositions)
        {
            var winningLongs = longPositions.Where(p => p.NetProfit > 0).ToList();
            var losingLongs = longPositions.Where(p => p.NetProfit <= 0).ToList();
            var winningShorts = shortPositions.Where(p => p.NetProfit > 0).ToList();
            var losingShorts = shortPositions.Where(p => p.NetProfit <= 0).ToList();

            await CheckTrimScenario(winningLongs, losingShorts, "Long winning, Short losing");
            await CheckTrimScenario(winningShorts, losingLongs, "Short winning, Long losing");
        }

        private async Task CheckTrimScenario(List<Position> winningPositions, List<Position> losingPositions, string scenario)
        {
            if (!winningPositions.Any() || !losingPositions.Any())
                return;

            if (PositionAnalyzer.ShouldTrimPositions(winningPositions, losingPositions, LosingPositionTrimPercentage))
            {
                var totalWinningProfit = winningPositions.Sum(p => p.NetProfit);
                var requiredProfit = PositionAnalyzer.CalculateRequiredProfit(losingPositions, LosingPositionTrimPercentage);
                
                if (EnableLogging)
                    Print($"Trim opportunity detected: {scenario}. Winning profit: {totalWinningProfit:F2}, Required: {requiredProfit:F2}");

                await ExecuteTrimStrategy(winningPositions, losingPositions);
            }
        }

        private async Task ExecuteTrimStrategy(List<Position> winningPositions, List<Position> losingPositions)
        {
            await CloseAllWinningPositions(winningPositions);
            await TrimLosingPositions(losingPositions);
        }

        private async Task CloseAllWinningPositions(List<Position> winningPositions)
        {
            foreach (var position in winningPositions)
            {
                await ClosePositionWithRetry(position, null, $"winning position {position.Id}");
            }
        }

        private async Task TrimLosingPositions(List<Position> losingPositions)
        {
            var currentPrice = Symbols.GetSymbol(MonitoredSymbol).Bid;
            var prioritizedPositions = PositionAnalyzer.PrioritizeLosingPositions(losingPositions, currentPrice);

            foreach (var position in prioritizedPositions)
            {
                var volumeToClose = PositionAnalyzer.CalculateVolumeToClose(position, LosingPositionTrimPercentage);
                
                if (volumeToClose > 0)
                {
                    await ClosePositionWithRetry(position, volumeToClose, $"losing position {position.Id}");
                }
            }
        }

        private async Task ClosePositionWithRetry(Position position, long? volumeToClose, string description)
        {
            var attempts = 0;
            
            while (attempts < MaxRetries)
            {
                try
                {
                    TradeResult result;
                    if (volumeToClose.HasValue)
                    {
                        result = await ClosePositionAsync(position, volumeToClose.Value);
                        if (EnableLogging)
                            Print($"Trimmed {description}: Closed Volume: {volumeToClose.Value}, Remaining: {position.VolumeInUnits - volumeToClose.Value}, Loss: {position.NetProfit:F2}");
                    }
                    else
                    {
                        result = await ClosePositionAsync(position);
                        if (EnableLogging)
                            Print($"Closed {description}: Volume: {position.VolumeInUnits}, Profit: {position.NetProfit:F2}");
                    }

                    if (result.IsSuccessful)
                    {
                        if (EnableDetailedLogging)
                            Print($"Successfully processed {description} on attempt {attempts + 1}");
                        return;
                    }
                    else
                    {
                        LogError($"Failed to close {description} on attempt {attempts + 1}: {result.Error}");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Exception closing {description} on attempt {attempts + 1}: {ex.Message}");
                }
                
                attempts++;
                if (attempts < MaxRetries)
                {
                    await Task.Delay(1000 * attempts);
                }
            }
            
            LogError($"Failed to close {description} after {MaxRetries} attempts");
        }

        private void LogError(string message)
        {
            Print($"ERROR: {message}");
        }

        private void LogPeriodically(string message)
        {
            if (DateTime.Now - _lastLogTime >= _logInterval)
            {
                if (EnableLogging)
                    Print($"STATUS: {message}");
                _lastLogTime = DateTime.Now;
            }
        }

        protected override void OnPositionOpened(PositionOpenedEventArgs args)
        {
            if (args.Position.SymbolName == MonitoredSymbol && EnableLogging)
            {
                Print($"New position opened: {args.Position.TradeType} {args.Position.VolumeInUnits} {args.Position.SymbolName}");
                
                if (EnableDetailedLogging)
                    Print($"Position Details: {PositionAnalyzer.GetPositionSummary(args.Position)}");
            }
        }

        protected override void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (args.Position.SymbolName == MonitoredSymbol && EnableLogging)
            {
                Print($"Position closed: {args.Position.TradeType} {args.Position.VolumeInUnits} {args.Position.SymbolName}, P&L: {args.Position.NetProfit:F2}");
                
                if (EnableDetailedLogging)
                    Print($"Final Position Details: {PositionAnalyzer.GetPositionSummary(args.Position)}");
            }
        }
    }
}