using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
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
            return (long)Math.Round(position.VolumeInUnits * trimPercentage);
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
}