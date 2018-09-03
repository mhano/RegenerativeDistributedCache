using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RegenerativeDistributedCache.Redis;

namespace RegenerativeDistributedCache.Demo
{
    class Program
    {
        private static readonly ConcurrentBag<Tuple<Task, TimeSpan>> MonitoredWorkBag = new ConcurrentBag<Tuple<Task, TimeSpan>>();

        private static int _measureSeq = 0;
        private static int _perTestMeasureEveryN = 1;

        static void Main(string[] args)
        {
            var rndSrc = new Random(2);

            int testIdSrc = 0;

            int uniqueCacheId = 1;
            var ttl = TimeSpan.FromSeconds(10); // 20 min equiv
            var regen = TimeSpan.FromSeconds(2.5); // 1 min equiv

            var regenDuration = TimeSpan.FromSeconds(1.25); // duration of call to generate value to store in cache
            var regenErrorPercentage = 0;
            var farmClockTollerance = 0.1;

            var p1Duration = TimeSpan.FromSeconds(10);
            var p1Frequency = TimeSpan.FromSeconds(0.5);

            bool doRegTest = true;
            bool doPerfTest = true;
            bool writeOutputFiles = false;

            var perfTestCount = 1000000;
            var perfTestMeasure = 100000;

            var keyspace = $"keyspace{Guid.NewGuid():N}";

            if (args.Length == 0 && !Debugger.IsAttached)
            {
                Process.Start(Assembly.GetEntryAssembly().Location, $"nospawn 1 {keyspace}");
                //Task.Delay(2000).Wait();
                Process.Start(Assembly.GetEntryAssembly().Location, $"nospawn 2 {keyspace}");
                return;
            }

            var id = 0;
            if (args.Length >= 3)
            {
                int.TryParse(args[1], out id);
                keyspace = args[2];
            }

            var synchedConsole = new SynchedColouredConsoleTraceWriter(writeOutputFiles ?
                $"C:\\temp\\SynchedConsole_{keyspace}_{id}.txt" :
                (string)null);

            using (var ct1R = new BasicRedisWrapper("localhost"))
            using (var cacheTest1 = new RegenerativeCacheManager("keyspace3", ct1R.Cache, ct1R.Lock, ct1R.Bus))
            using (var ct2R = new BasicRedisWrapper("localhost"))
            using (var cacheTest2 = new RegenerativeCacheManager("Keyspace3", ct2R.Cache, ct2R.Lock, ct2R.Bus))
            {
                new List<IDisposable> { cacheTest1, ct1R, cacheTest2, ct2R }.ForEach(d => d.Dispose());
            }
            synchedConsole.WriteLine("RegenerativeCacheManager dispose works.", ConsoleColor.Black, ConsoleColor.Green);

            var redis = new BasicRedisWrapper("localhost", true);
            var cache = new RegenerativeCacheManager(keyspace, redis.Cache, redis.Lock, redis.Bus, synchedConsole)
            {
                // low tollerences in this test due to extremely low cache item re-generation and expiry times, normally 30s to minutes
                CacheExpiryToleranceSeconds = 1,
                TriggerDelaySeconds = 1,
                MinimumForwardSchedulingSeconds = 1,
                FarmClockToleranceSeconds = farmClockTollerance,
            };

            var generateFunc = new Func<string>(() =>
            {
                Task.Delay(regenDuration).Wait();
                if (rndSrc.Next(0, 100) < regenErrorPercentage) throw new ApplicationException("Synthetic Error");
                return $" >>> CacheItemVal_{DateTime.Now:ss.fff}_{Interlocked.Increment(ref uniqueCacheId)} <<< ";
            });

            if (doRegTest)
            {
                synchedConsole.WriteLine($"Inactive TTL: {ttl.TotalSeconds}s, Regeneration every: {regen.TotalSeconds}s, Regeneration performance: {regenDuration.TotalSeconds}s");
                synchedConsole.WriteLine($"------- running get per second (in background) for {p1Duration.TotalSeconds} seconds");
                synchedConsole.WriteLine("=================================================");
                var start = DateTime.UtcNow;
                while (DateTime.UtcNow.Subtract(start).TotalSeconds < p1Duration.TotalSeconds)
                {
                    var testId = Interlocked.Increment(ref testIdSrc);
                    Task.Run(() => synchedConsole.WriteLine($"* PART 1 * Cache Value: {MonitorWork(() => cache.GetOrAdd("test1", generateFunc, ttl, regen), synchedConsole, testId)}"));
                    Task.Delay(p1Frequency).Wait();
                }

                synchedConsole.WriteLine("=================================================");
                synchedConsole.DebugWait(6);
                var testX1 = Interlocked.Increment(ref testIdSrc);
                synchedConsole.WriteLine($"* PART 2 * Cache Value: {MonitorWork(() => cache.GetOrAdd("test1", generateFunc, ttl, regen), synchedConsole, testX1)}");

                synchedConsole.WriteLine("=================================================");
                synchedConsole.DebugWait(15);
                var testX2 = Interlocked.Increment(ref testIdSrc);
                Task.Run(() => synchedConsole.WriteLine($"* PART 3 * Cache Value: {MonitorWork(() => cache.GetOrAdd("test1", generateFunc, ttl, regen), synchedConsole, testX2)}"));
                var testX3 = Interlocked.Increment(ref testIdSrc);
                Task.Run(() => synchedConsole.WriteLine($"* PART 3 * Cache Value: {MonitorWork(() => cache.GetOrAdd("test1", generateFunc, ttl, regen), synchedConsole, testX3)}"));
                var testX4 = Interlocked.Increment(ref testIdSrc);
                synchedConsole.WriteLine($"* PART 3 * Cache Value: {MonitorWork(() => cache.GetOrAdd("test1", generateFunc, ttl, regen), synchedConsole, testX4)}");
                var testX5 = Interlocked.Increment(ref testIdSrc);
                synchedConsole.WriteLine($"* PART 3 * Cache Value: {MonitorWork(() => cache.GetOrAdd("test1", generateFunc, ttl, regen), synchedConsole, testX5)}");
                synchedConsole.DebugWait(2);
                synchedConsole.DebugWait(20);

                ShowStats(synchedConsole);

                try
                {
                    Task.WaitAll(MonitoredWorkBag.Select(t => t.Item1).ToArray());
                }
                catch (Exception ex)
                {
                    synchedConsole.WriteLine($"First error: {(ex as AggregateException)?.InnerExceptions.First().ToString() ?? ex.ToString()}", ConsoleColor.White, ConsoleColor.Red);
                }

            }

            if (doRegTest && doPerfTest) synchedConsole.OpenNewOutputFile();

            if (doPerfTest)
            {
                cache.Dispose();

                _perTestMeasureEveryN = Math.Max(1, perfTestCount / perfTestMeasure);

                cache = new RegenerativeCacheManager(keyspace, redis.Cache, redis.Lock, redis.Bus, synchedConsole)
                {
                    // low tollerences in this test due to extremely low cache item re-generation and expiry times, normally 30s to minutes
                    TriggerDelaySeconds = 1,
                    MinimumForwardSchedulingSeconds = 1,
                    FarmClockToleranceSeconds = farmClockTollerance,
                    CacheExpiryToleranceSeconds = 5,
                };

                synchedConsole.WriteLine($"Running performance test of {perfTestCount:#,##0} cache calls to GetOrAdd in parallel.", ConsoleColor.White, ConsoleColor.DarkBlue);

                testIdSrc = 1;

                while (MonitoredWorkBag.TryTake(out Tuple<Task, TimeSpan> tr)) ;

                synchedConsole.ShowFullOutputToConsole = false;
                // warm up
                cache.GetOrAdd("test1", generateFunc, ttl, regen);

                var swWait = Stopwatch.StartNew();

                var dop = 10;
                Parallel.ForEach(Enumerable.Range(0, dop), new ParallelOptions { MaxDegreeOfParallelism = dop }, (i) =>
                {
                    for (int j = 0; j < perfTestCount / dop; j++)
                    {
                        var testId = Interlocked.Increment(ref testIdSrc);
                        if (j % 10 == 0) Thread.Sleep(1);
                        synchedConsole.WriteLine($"* PART 4 * Cache Value: {MonitorWork(() => cache.GetOrAdd("test1", generateFunc, ttl, regen), synchedConsole, testId)}");
                    }
                });

                swWait.Stop();

                synchedConsole.WriteLine($"\r\n\r\n\t\t\t{perfTestCount:#,##0} cache GetOrAdd calls completed with {dop} degrees of parallelism." +
                                         $"\r\n\r\n\t\t\t Duration: {swWait.Elapsed.TotalMilliseconds * 1000:#,##0.0}us." +
                                         $"\r\n\t\t\t Requests/s: {perfTestCount / swWait.Elapsed.TotalSeconds:#,##0.0} ({perfTestCount / swWait.Elapsed.TotalHours:#,##0}/h)" +
                                         $"\r\n\r\n"
                                         , ConsoleColor.White, overrideShowOutput: true);

                ShowStats(synchedConsole);

                while (MonitoredWorkBag.TryTake(out Tuple<Task, TimeSpan> tr)) { tr.Item1.Dispose(); }

                GC.Collect();
            }

            synchedConsole.WriteLine("Press enter to exit.", overrideShowOutput: true);

            synchedConsole.CloseAndStopAllWriting();

            Console.ReadLine();
        }

        private static void ShowStats(SynchedColouredConsoleTraceWriter synchedConsole)
        {
            synchedConsole.WriteLine($@"

Following results & performance based on a {100.0 / _perTestMeasureEveryN:#0.0}% sample ({MonitoredWorkBag.Count:#,##0}) + all timeouts & faulted requests.

Results & performance of sample...
                        Total: {MonitoredWorkBag.Count:#,##0}, 
                        Completed: {MonitoredWorkBag.Count(t => t.Item1.IsCompleted):#,##0}, 
                        Faulted: {MonitoredWorkBag.Count(t => t.Item1.IsFaulted):#,##0}, 
                        Cancelled: {MonitoredWorkBag.Count(t => t.Item1.IsCanceled):#,##0},
            
                Performance (microseconds [us] - thousandaths of milliseconds / millionths of seconds)

                        Fastest: {MonitoredWorkBag.Min(t => t.Item2.TotalMilliseconds * 1000):#,##0.0} (us),
                        Slowest: {MonitoredWorkBag.Max(t => t.Item2.TotalMilliseconds * 1000):#,##0.0} (us),
                    
                        Average time: {MonitoredWorkBag.Average(t => t.Item2.TotalMilliseconds * 1000):#,##0.0} (us),
                        Median: {Percentile(MonitoredWorkBag.Select(t => t.Item2.TotalMilliseconds * 1000), 0.50):#,##0.0} (us),
                    
                        P75: {Percentile(MonitoredWorkBag.Select(t => t.Item2.TotalMilliseconds * 1000), 0.75):#,##0.0} (us),
                        P90: {Percentile(MonitoredWorkBag.Select(t => t.Item2.TotalMilliseconds * 1000), 0.90):#,##0.0} (us),
                        P95: {Percentile(MonitoredWorkBag.Select(t => t.Item2.TotalMilliseconds * 1000), 0.95):#,##0.0} (us),
                        P99: {Percentile(MonitoredWorkBag.Select(t => t.Item2.TotalMilliseconds * 1000), 0.99):#,##0.0} (us),
                        P3x9: {Percentile(MonitoredWorkBag.Select(t => t.Item2.TotalMilliseconds * 1000), 0.999):#,##0.0} (us),
                        P5x9: {Percentile(MonitoredWorkBag.Select(t => t.Item2.TotalMilliseconds * 1000), 0.99999):#,##0.0} (us),

", overrideShowOutput: true);
        }

        public static double Percentile(IEnumerable<double> seq, double percentile)
        {
            var elements = seq.ToArray();
            Array.Sort(elements);
            double realIndex = percentile * (elements.Length - 1);
            int index = (int)realIndex;
            double frac = realIndex - index;
            if (index + 1 < elements.Length)
                return elements[index] * (1 - frac) + elements[index + 1] * frac;
            else
                return elements[index];
        }

        private static string MonitorWork(Func<string> work, SynchedColouredConsoleTraceWriter synchedConsole, int testId = -3)
        {
            var sw = new Stopwatch();

            Task<string> task = null;

            try
            {
                sw.Start();
                var result = work();
                sw.Stop();

                task = Task.FromResult<string>(result);

                return task.Result;
            }
            catch (Exception ex)
            {
                sw.Stop();

                task = Task.FromException<string>(ex);

                var error = $"ERROR: {(ex as AggregateException)?.InnerExceptions.First().Message ?? ex.Message}";
                synchedConsole.WriteLine($"MonitorWork: * *** *** ***   {error}", ConsoleColor.White, ConsoleColor.Red);
                return error;
            }
            finally
            {
                synchedConsole.WriteLine($"MonitorWork: testId:{testId}: {sw.Elapsed.TotalMilliseconds * 1000:#,##0.0}(us)", ConsoleColor.White, ConsoleColor.Blue);
                task = task ?? Task.FromCanceled<string>(CancellationToken.None);

                if (task.IsCanceled || task.IsFaulted || (Interlocked.Increment(ref _measureSeq) % _perTestMeasureEveryN) == 0)
                {
                    MonitoredWorkBag.Add(new Tuple<Task, TimeSpan>(task, sw.Elapsed));
                }
            }
        }
    }

}
