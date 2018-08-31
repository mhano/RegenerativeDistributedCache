using System;
using RegenerativeDistributedCache.Redis;

namespace RegenDistCache.Tests.DynamicSkippableTests
{
    public class TestMachineHasRedis
    {
        public const string RedisConfigurationEnvironmentVariable = "REDIS_CONFIGURATION_FOR_UNIT_TESTS";

        private const string DefaultLocalRedisConfiguration = "localhost"; // :6379

        private static readonly object LocalRedisTestSynch = new object();

        private static bool _localRedisTested = false;
        private static bool _localRedisAvailable = false;
        private static bool _localRedisConfiguredForTests = false;
        private static DateTime _localRedisTestTime = DateTime.MinValue;
        private static string _localRedisTestFailDetails = null;
        private static string _redisConfiguration = null;

        /// <summary>
        /// Tests local redis connection (at app domain startup and returns redis configuration
        /// string for testing (from environment variable REDIS_CONFIGURATION_FOR_UNIT_TESTS else localhost).
        /// Throws application exception if redis connection is not possible.
        /// </summary>
        /// <returns>redis configuration string</returns>
        /// <throws>redis configuration string</throws>
        public static string GetTestEnvironmentRedis()
        {
            EnsureRedisCheckedIfNeeded();

            if (!_localRedisAvailable)
            {
                throw new ApplicationException(GetRedisStartupError());
            }
            else
            {
                return _redisConfiguration;
            }
        }

        private static string GetRedisStartupError()
        {
            return $"Error connecting to redis at test app domain startup: " +
                   $"Redis configuration: {_redisConfiguration}, " +
                   $"When: {_localRedisTestTime}, " +
                   $"Error: {_localRedisTestFailDetails}";
        }

        /// <summary>
        /// Throws exception when we need to skip tests based on a live redis
        /// connection in a test environment, only when it's not configured 
        /// via environment variable REDIS_CONFIGURATION_FOR_UNIT_TESTS thus we 
        /// have defaulted localhost, AND we couldn't connect to localhost.
        /// Note - release build environment does have the env variable set.
        /// </summary>
        public static void SkipLiveRedisBasedTests()
        {
            EnsureRedisCheckedIfNeeded();

            if (!_localRedisConfiguredForTests && !_localRedisAvailable)
            {
                throw new ApplicationException(GetRedisStartupError());
            }
        }

        private static void EnsureRedisCheckedIfNeeded()
        {
            if (_localRedisTested) return;

            lock (LocalRedisTestSynch)
            {
                if (_localRedisTested) return;

                try
                {
                    var environmentVariableRedisConfig = Environment.GetEnvironmentVariable(RedisConfigurationEnvironmentVariable);
                    //environmentVariableRedisConfig = "";
                    _redisConfiguration = string.IsNullOrWhiteSpace(environmentVariableRedisConfig) ? DefaultLocalRedisConfiguration : environmentVariableRedisConfig;

                    // Skip tests instead of throwing errors if we are defaulting to localhost
                    // if we have configured redis via REDIS_CONFIGURATION_FOR_UNIT_TESTS
                    // we want a failed build/tests.
                    _localRedisConfiguredForTests = !string.IsNullOrWhiteSpace(environmentVariableRedisConfig);

                    _localRedisTestTime = DateTime.Now;
                    using (var basicRedis = new BasicRedisWrapper(_redisConfiguration, false))
                    {
                        // TODO: Consider - should we verify that the local redis is capable of basic tasks we need (locking/pubsub/etc.)?

                        basicRedis.Cache.StringSet(
                            $"{typeof(TestMachineHasRedis).FullName}:{nameof(GetTestEnvironmentRedis)}:{Guid.NewGuid():N}",
                            "value", TimeSpan.FromSeconds(3));

                        _localRedisAvailable = true;
                    }
                }
                catch(Exception ex)
                {
                    _localRedisTestFailDetails = ex.ToString();
                    
                    _localRedisAvailable = false;
                }

                _localRedisTested = true;
            }
        }
    }
}