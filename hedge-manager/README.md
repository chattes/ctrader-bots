# cTrader Hedging Manager Bot

A sophisticated automated trading bot for cTrader that manages hedged positions by automatically trimming losing positions when winning positions have sufficient profit.

## Overview

The Hedging Manager Bot monitors open positions for a specific currency pair and automatically executes position trimming when the following conditions are met:

1. **Hedging Detection**: Both long and short positions exist for the monitored symbol
2. **Profit Threshold**: Winning positions have accumulated enough profit to cover 75% (configurable) of the losing positions' losses
3. **Position Prioritization**: When multiple losing positions exist, the bot prioritizes closing the position that is:
   - Furthest from the current price
   - Has the highest loss magnitude

## Key Features

- **Automated Hedging Detection**: Only activates when both long and short positions exist
- **Intelligent Position Prioritization**: Uses distance from current price and loss magnitude to determine which losing positions to close first
- **Partial Position Closing**: Closes winning positions completely and trims 75% of losing positions
- **Comprehensive Error Handling**: Includes retry logic and detailed error reporting
- **Configurable Parameters**: All key settings can be customized through the bot parameters
- **Detailed Logging**: Multiple logging levels for monitoring and debugging

## Files Structure

- `HedgingManagerBot.cs` - Main bot implementation
- `PositionAnalyzer.cs` - Utility class for position analysis and calculations
- `README.md` - This documentation file

## Configuration Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| **Symbol** | EURUSD | Currency pair to monitor |
| **Check Interval (seconds)** | 1 | How often to check positions |
| **Losing Position Trim Percentage** | 0.75 | Percentage of losing position to close (0.1-0.95) |
| **Enable Logging** | true | Enable basic logging |
| **Enable Detailed Logging** | false | Enable verbose logging with position details |
| **Max Position Close Retries** | 3 | Number of retry attempts for position closing |

## How It Works

### 1. Position Monitoring
The bot continuously monitors all open positions for the specified currency pair at the configured interval.

### 2. Hedging Detection
- Identifies when both long and short positions exist
- Calculates total exposure and profit/loss for each direction
- Only proceeds if true hedging is detected

### 3. Profit/Loss Analysis
- Separates positions into winning and losing categories
- Calculates total profit from winning positions
- Calculates total loss from losing positions
- Determines if profit is sufficient to cover the configured percentage of losses

### 4. Position Prioritization
When multiple losing positions exist, the bot prioritizes them using:
- **Distance Factor (60% weight)**: Distance from current market price
- **Loss Factor (40% weight)**: Magnitude of the loss

### 5. Execution Strategy
When trim conditions are met:
1. **Close all winning positions** completely
2. **Trim losing positions** by the configured percentage (default 75%)
3. **Retry failed operations** up to the maximum retry count

## Usage Instructions

### Installation
1. Copy `HedgingManagerBot.cs` and `PositionAnalyzer.cs` to your cTrader cBots folder
2. Compile the bot in cTrader
3. Add the bot to your chart with the desired currency pair

### Configuration
1. Set the **Symbol** parameter to your desired currency pair (e.g., "EURUSD", "GBPUSD")
2. Adjust the **Check Interval** based on your trading strategy (1-60 seconds recommended)
3. Configure the **Losing Position Trim Percentage** (0.75 = 75% of losing positions closed)
4. Enable logging as needed for monitoring

### Best Practices
1. **Start with paper trading** to test the bot's behavior
2. **Monitor the logs** to understand the bot's decision-making process
3. **Adjust trim percentage** based on your risk tolerance
4. **Use detailed logging** initially to understand position prioritization

## Example Scenarios

### Scenario 1: Simple Hedging
- Long position: 1.0 lot EURUSD, +$50 profit
- Short position: 1.0 lot EURUSD, -$60 loss
- Trim threshold: 75% of $60 = $45
- **Action**: Close long position (+$50), trim 0.75 lots of short position
- **Result**: Remaining 0.25 lots short position, net break-even

### Scenario 2: Multiple Losing Positions
- Long position: 1.0 lot EURUSD, +$100 profit
- Short position 1: 0.5 lots EURUSD, -$40 loss, entry at 1.1200 (current price 1.1150)
- Short position 2: 0.5 lots EURUSD, -$60 loss, entry at 1.1250 (current price 1.1150)
- **Priority**: Position 2 (further from current price and higher loss)
- **Action**: Close long position, trim 0.375 lots from position 2 first, then 0.375 lots from position 1

## Error Handling

The bot includes comprehensive error handling:
- **Retry Logic**: Failed position closures are retried up to 3 times with exponential backoff
- **Exception Handling**: All major operations are wrapped in try-catch blocks
- **Detailed Logging**: Errors are logged with context for debugging
- **Graceful Degradation**: Bot continues operating even if individual operations fail

## Logging Levels

### Basic Logging (Default)
- Position open/close events
- Trim opportunities detected
- Successful position closures
- Errors and warnings

### Detailed Logging
- Complete position analysis
- Hedging statistics
- Position prioritization details
- Retry attempts and outcomes
- Performance metrics

## Risk Considerations

1. **Market Volatility**: Rapid price movements may affect execution
2. **Slippage**: Position closures may not execute at expected prices
3. **Partial Fills**: Large positions may be partially filled
4. **Connection Issues**: Network problems may affect trade execution

## Troubleshooting

### Common Issues
1. **Bot not activating**: Ensure both long and short positions exist for the monitored symbol
2. **Positions not closing**: Check for sufficient account balance and verify position IDs
3. **Logging not showing**: Verify logging parameters are enabled

### Debug Steps
1. Enable detailed logging
2. Monitor the cTrader log window
3. Check position status in the Positions tab
4. Verify bot parameters are correctly set

## Technical Notes

- **Language**: C# (.NET)
- **Platform**: cTrader cBot
- **Threading**: Uses locks to prevent race conditions
- **Async Operations**: Position closing operations are asynchronous
- **Timer-based**: Uses cTrader's Timer class for periodic monitoring

## Support

For questions or issues, please refer to the cTrader documentation or community forums.

## Version History

- **v1.0**: Initial release with basic hedging management
- **v1.1**: Added position prioritization and enhanced error handling
- **v1.2**: Integrated PositionAnalyzer utility class and improved logging