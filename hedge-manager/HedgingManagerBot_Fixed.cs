using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    // Position Analysis Utility Classes and Methods
    public static class PositionAnalyzer
    {
        public class HedgingInfo
        {
            public List<Position> LongPositions { get; set; }
            public List<Position> ShortPositions { get; set; }
            public double TotalLongVolume { get; set; }
            public double TotalShortVolume { get; set; }
            public double NetExposure { get; set; }
            public double TotalProfit { get; set; }
            public bool IsHedged { get; set; }
        }

        public class PositionMetrics
        {
            public double DistanceFromCurrentPrice { get; set; }
            public double LossPercentage { get; set; }
            public double Priority { get; set; }
            public Position Position { get; set; }
        }

        public static HedgingInfo AnalyzeHedging(IEnumerable<Position> positions)
        {
            var positionList = positions.ToList();
            var longPositions = positionList.Where(p => p.TradeType == TradeType.Buy).ToList();
            var shortPositions = positionList.Where(p => p.TradeType == TradeType.Sell).ToList();

            var totalLongVolume = longPositions.Sum(p => p.VolumeInUnits);
            var totalShortVolume = shortPositions.Sum(p => p.VolumeInUnits);
            var netExposure = totalLongVolume - totalShortVolume;
            var totalProfit = positionList.Sum(p => p.NetProfit);

            return new HedgingInfo
            {
                LongPositions = longPositions,
                ShortPositions = shortPositions,
                TotalLongVolume = totalLongVolume,
                TotalShortVolume = totalShortVolume,
                NetExposure = netExposure,
                TotalProfit = totalProfit,
                IsHedged = longPositions.Any() && shortPositions.Any()
            };
        }

        public static List<PositionMetrics> CalculatePositionMetrics(IEnumerable<Position> positions, double currentPrice)
        {
            return positions.Select(p => new PositionMetrics
            {
                Position = p,
                DistanceFromCurrentPrice = CalculateDistanceFromCurrentPrice(p, currentPrice),
                LossPercentage = CalculateLossPercentage(p),
                Priority = CalculatePriority(p, currentPrice)
            }).ToList();
        }

        public static double CalculateDistanceFromCurrentPrice(Position position, double currentPrice)
        {
            return Math.Abs(position.EntryPrice - currentPrice);
        }

        public static double CalculateLossPercentage(Position position)
        {
            if (position.NetProfit >= 0)
                return 0;

            var investedAmount = position.VolumeInUnits * position.EntryPrice;
            return Math.Abs(position.NetProfit) / investedAmount * 100;
        }

        public static double CalculatePriority(Position position, double currentPrice)
        {
            var distanceWeight = 0.6;
            var lossWeight = 0.4;

            var distanceFromEntry = Math.Abs(position.EntryPrice - currentPrice);
            var lossMagnitude = Math.Abs(position.NetProfit);

            return distanceFromEntry * distanceWeight + lossMagnitude * lossWeight;
        }

        public static List<Position> PrioritizeLosingPositions(IEnumerable<Position> losingPositions, double currentPrice)
        {
            var metrics = CalculatePositionMetrics(losingPositions, currentPrice);
            return metrics.OrderByDescending(m => m.Priority).Select(m => m.Position).ToList();
        }

        public static bool ShouldTrimPositions(IEnumerable<Position> winningPositions, IEnumerable<Position> losingPositions, double trimPercentage)
        {
            var totalWinningProfit = winningPositions.Sum(p => p.NetProfit);
            var totalLosingLoss = Math.Abs(losingPositions.Sum(p => p.NetProfit));

            return totalWinningProfit >= totalLosingLoss * trimPercentage;
        }

        public static double CalculateRequiredProfit(IEnumerable<Position> losingPositions, double trimPercentage)
        {
            var totalLosingLoss = Math.Abs(losingPositions.Sum(p => p.NetProfit));
            return totalLosingLoss * trimPercentage;
        }

        public static long CalculateVolumeToClose(Position position, double trimPercentage)
        {
            // Convert to lots for calculation
            var positionLots = (double)position.VolumeInUnits / 100000.0; // Convert units to lots
            var targetLotsToClose = positionLots * trimPercentage;
            
            // Round to nearest 0.01 lot increment (minimum lot size)
            var roundedLots = Math.Round(targetLotsToClose, 2);
            
            // Convert back to units
            var volumeToClose = (long)(roundedLots * 100000);
            
            // Ensure it's within position bounds
            if (volumeToClose >= position.VolumeInUnits)
            {
                return (long)position.VolumeInUnits; // Close entire position
            }
            
            // Ensure minimum lot size (0.01 = 1000 units)
            if (volumeToClose < 1000L)
            {
                return (long)position.VolumeInUnits; // Close entire position if too small
            }
            
            return volumeToClose;
        }

        public static string GetPositionSummary(Position position)
        {
            return $"ID: {position.Id}, Type: {position.TradeType}, Volume: {position.VolumeInUnits:N0}, " +
                   $"Entry: {position.EntryPrice:F5}, P&L: {position.NetProfit:F2}, " +
                   $"Pips: {position.Pips:F1}";
        }

        public static string GetHedgingSummary(HedgingInfo hedgingInfo)
        {
            return $"Long Volume: {hedgingInfo.TotalLongVolume:N0}, Short Volume: {hedgingInfo.TotalShortVolume:N0}, " +
                   $"Net Exposure: {hedgingInfo.NetExposure:N0}, Total P&L: {hedgingInfo.TotalProfit:F2}, " +
                   $"Is Hedged: {hedgingInfo.IsHedged}";
        }

        public static bool IsPositionProfitable(Position position, double minimumProfit = 0)
        {
            return position.NetProfit > minimumProfit;
        }

        public static bool IsPositionLosing(Position position, double maximumLoss = 0)
        {
            return position.NetProfit < maximumLoss;
        }

        public static double GetBreakEvenPrice(IEnumerable<Position> positions)
        {
            var positionList = positions.ToList();
            if (!positionList.Any())
                return 0;

            var totalWeightedPrice = positionList.Sum(p => p.EntryPrice * p.VolumeInUnits);
            var totalVolume = positionList.Sum(p => p.VolumeInUnits);

            return totalWeightedPrice / totalVolume;
        }
    }

    // Main Hedging Manager Bot
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

        private readonly object _lockObject = new object();
        private DateTime _lastLogTime = DateTime.MinValue;
        private readonly TimeSpan _logInterval = TimeSpan.FromMinutes(1);

        protected override void OnStart()
        {
            if (EnableLogging)
                Print($"HedgingManagerBot started for symbol: {MonitoredSymbol}");

            ValidateParameters();
            StartMonitoring();
            
            // Subscribe to position events
            Positions.Opened += OnPositionOpened;
            Positions.Closed += OnPositionClosed;
        }

        protected override void OnStop()
        {
            // Stop the timer
            Timer.Stop();
            
            // Unsubscribe from position events
            Positions.Opened -= OnPositionOpened;
            Positions.Closed -= OnPositionClosed;

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
            // cTrader Timer API: Start timer with interval in seconds
            Timer.Start(CheckInterval);
        }

        protected override void OnTimer()
        {
            // This method is called automatically at each timer interval
            MonitorPositions();
        }

        private void MonitorPositions()
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

                CheckForTrimOpportunity(hedgingInfo.LongPositions, hedgingInfo.ShortPositions);
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

        private List<Position> GetSymbolPositions()
        {
            return Positions.Where(p => p.SymbolName == MonitoredSymbol).ToList();
        }

        private void CheckForTrimOpportunity(List<Position> longPositions, List<Position> shortPositions)
        {
            var winningLongs = longPositions.Where(p => p.NetProfit > 0).ToList();
            var losingLongs = longPositions.Where(p => p.NetProfit <= 0).ToList();
            var winningShorts = shortPositions.Where(p => p.NetProfit > 0).ToList();
            var losingShorts = shortPositions.Where(p => p.NetProfit <= 0).ToList();

            CheckTrimScenario(winningLongs, losingShorts, "Long winning, Short losing");
            CheckTrimScenario(winningShorts, losingLongs, "Short winning, Long losing");
        }

        private void CheckTrimScenario(List<Position> winningPositions, List<Position> losingPositions, string scenario)
        {
            if (!winningPositions.Any() || !losingPositions.Any())
                return;

            if (PositionAnalyzer.ShouldTrimPositions(winningPositions, losingPositions, LosingPositionTrimPercentage))
            {
                var totalWinningProfit = winningPositions.Sum(p => p.NetProfit);
                var requiredProfit = PositionAnalyzer.CalculateRequiredProfit(losingPositions, LosingPositionTrimPercentage);
                
                if (EnableLogging)
                    Print($"Trim opportunity detected: {scenario}. Winning profit: {totalWinningProfit:F2}, Required: {requiredProfit:F2}");

                ExecuteTrimStrategy(winningPositions, losingPositions);
            }
        }

        private void ExecuteTrimStrategy(List<Position> winningPositions, List<Position> losingPositions)
        {
            CloseAllWinningPositions(winningPositions);
            TrimLosingPositions(losingPositions);
        }

        private void CloseAllWinningPositions(List<Position> winningPositions)
        {
            foreach (var position in winningPositions)
            {
                ClosePositionWithRetry(position, null, $"winning position {position.Id}");
            }
        }

        private void TrimLosingPositions(List<Position> losingPositions)
        {
            var symbol = Symbols.GetSymbol(MonitoredSymbol);
            var currentPrice = symbol.Bid;
            var prioritizedPositions = PositionAnalyzer.PrioritizeLosingPositions(losingPositions, currentPrice);

            foreach (var position in prioritizedPositions)
            {
                var volumeToClose = PositionAnalyzer.CalculateVolumeToClose(position, LosingPositionTrimPercentage);
                
                // If calculated volume equals position volume, close entire position
                if (volumeToClose >= position.VolumeInUnits)
                {
                    if (EnableLogging)
                        Print($"Calculated volume {volumeToClose} >= position volume {position.VolumeInUnits}, closing entire position {position.Id}");
                    ClosePositionWithRetry(position, null, $"losing position {position.Id}");
                }
                else if (volumeToClose > 0)
                {
                    var lotsToClose = (double)volumeToClose / 100000.0;
                    if (EnableDetailedLogging)
                        Print($"Trimming position {position.Id}: {volumeToClose} units ({lotsToClose:F2} lots) from {position.VolumeInUnits} units ({(double)position.VolumeInUnits/100000.0:F2} lots)");
                    ClosePositionWithRetry(position, volumeToClose, $"losing position {position.Id}");
                }
                else
                {
                    if (EnableLogging)
                        Print($"Calculated volume {volumeToClose} invalid for position {position.Id}, skipping");
                }
            }
        }

        private bool IsValidPartialCloseVolume(Position position, long volumeToClose)
        {
            try
            {
                var symbol = Symbols.GetSymbol(position.SymbolName);
                var minVolume = symbol.VolumeInUnitsMin;
                
                // Check if volume to close meets minimum requirements
                if (volumeToClose < minVolume)
                {
                    if (EnableDetailedLogging)
                        Print($"Volume to close {volumeToClose} is below minimum {minVolume}");
                    return false;
                }
                
                // Check if remaining volume after close meets minimum requirements
                var remainingVolume = position.VolumeInUnits - volumeToClose;
                if (remainingVolume > 0 && remainingVolume < minVolume)
                {
                    if (EnableDetailedLogging)
                        Print($"Remaining volume {remainingVolume} would be below minimum {minVolume}");
                    return false;
                }
                
                // Check if volume to close exceeds position volume
                if (volumeToClose > position.VolumeInUnits)
                {
                    if (EnableDetailedLogging)
                        Print($"Volume to close {volumeToClose} exceeds position volume {position.VolumeInUnits}");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                if (EnableDetailedLogging)
                    Print($"Error validating volume: {ex.Message}");
                return false;
            }
        }

        private void ClosePositionWithRetry(Position position, long? volumeToClose, string description)
        {
            var attempts = 0;
            
            while (attempts < MaxRetries)
            {
                try
                {
                    TradeResult result;
                    if (volumeToClose.HasValue)
                    {
                        result = ClosePosition(position, volumeToClose.Value);
                        if (EnableLogging)
                            Print($"Trimmed {description}: Closed Volume: {volumeToClose.Value}, Remaining: {position.VolumeInUnits - volumeToClose.Value}, Loss: {position.NetProfit:F2}");
                    }
                    else
                    {
                        result = ClosePosition(position);
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
                    System.Threading.Thread.Sleep(1000 * attempts);
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

        private void OnPositionOpened(PositionOpenedEventArgs args)
        {
            if (args.Position.SymbolName == MonitoredSymbol && EnableLogging)
            {
                Print($"New position opened: {args.Position.TradeType} {args.Position.VolumeInUnits} {args.Position.SymbolName}");
                
                if (EnableDetailedLogging)
                    Print($"Position Details: {PositionAnalyzer.GetPositionSummary(args.Position)}");
            }
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
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