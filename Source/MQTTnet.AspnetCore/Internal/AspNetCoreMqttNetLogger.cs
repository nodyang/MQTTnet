using Microsoft.Extensions.Logging;
using MQTTnet.Diagnostics.Logger;
using System;

namespace MQTTnet.AspNetCore.Internal
{
    sealed class AspNetCoreMqttNetLogger : IMqttNetLogger
    {
        private readonly ILoggerFactory _loggerFactory;
        private const string categoryNamePrefix = "MQTTnet.AspNetCore.";

        public bool IsEnabled => true;

        public AspNetCoreMqttNetLogger(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public void Publish(MqttNetLogLevel logLevel, string source, string message, object[] parameters, Exception exception)
        {
            var logger = _loggerFactory.CreateLogger($"{categoryNamePrefix}{source}");
            logger.Log(CastLogLevel(logLevel), exception, message, parameters);
        }

        private static LogLevel CastLogLevel(MqttNetLogLevel level)
        {
            return level switch
            {
                MqttNetLogLevel.Verbose => LogLevel.Trace,
                MqttNetLogLevel.Info => LogLevel.Information,
                MqttNetLogLevel.Warning => LogLevel.Warning,
                MqttNetLogLevel.Error => LogLevel.Error,
                _ => LogLevel.None
            };
        }
    }
}
