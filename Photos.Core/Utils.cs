using Photos.Lib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Photos.Core
{
    public static class Utils
    {
        public const string AppName = "μPhotos";
        public const string LatinAppName = "uPhotos";

        public static Func<bool> TraceEnabled;
        public static Func<bool> ErrorReportOptOut;

        static Utils()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError((Exception)e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) => LogError(e.Exception);
        }

        private static readonly HttpClient HttpClient = new();

        public static async Task WaitWhile(Func<bool> cond)
        {
            while (cond())
                await Task.Delay(10);
        }

        public static Task DieOnError(this Task t)
        {
            return t.ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    LogError(t.Exception);
                    Environment.Exit(1);
                }
            });
        }

        static readonly byte[] buf = [104, 116, 116, 112, 115, 58, 47, 47, 99, 117, 98, 101, 119, 101, 97, 118, 101, 114, 46, 99, 111, 
            109, 47, 97, 112, 112, 47, 95, 97, 112, 105, 47, 76, 111, 103]; // to prevent spam
        public static void LogError(Exception e)
        {
            var txt = e?.ToString();
            Log(txt);
            if (ErrorReportOptOut?.Invoke() != true && !Debugger.IsAttached)
            {
                try
                {
                    var arg = new string[] { $"P_VER {MainView.Version}, {Environment.OSVersion}: {e?.Message}", txt, "true" };
                    HttpClient.PostAsJsonAsync(Encoding.UTF8.GetString(buf), arg, MySerializerContext.Default.StringArray).ContinueWith(x => x.IsFaulted).Wait(2000);
                }
                catch (Exception ex) { Log($"report failed {ex}"); }
            }
        }

        public static void TraceError(Exception e)
        {
            Trace("trace: " + e?.ToString());
        }

        public static void ForEach<T>(this IEnumerable<T> items, Action<T> act)
        {
            foreach (var item in items)
                act(item);
        }

        public static T[] AddToArr<T>(this T[] arr, T v)
        {
            Array.Resize(ref arr, arr.Length + 1);
            arr[^1] = v;
            return arr;
        }

        public static Func<T> Adjust<T>(this Func<T> baseF, Func<T, T> f)
        {
            var lastFunc = baseF;
            return () => f(lastFunc());
        }

        public static string LogFile;
        public static void Log(string s, string logFile = null)
        {
            try
            {
                logFile ??= LogFile;
                var msg = $"{DateTime.Now:O}: {s}\n";
                if (logFile != null)
                    File.AppendAllText(logFile, msg);

                Debug.WriteLine(msg);
            }
            catch { }
        }

        public static void Trace(string s)
        {
            if (TraceEnabled?.Invoke() == true)
                Log(s);
        }

        public static void PrintUtcMs(this DateTime d, string text = "time", int threshold = 0, bool flush = false)
        {
            var ms = d.GetUtcMs();
            if (ms >= threshold)
                Trace($"{text} - {ms}ms");
        }

        public static int GetUtcMs(this DateTime d) => (int)(DateTime.UtcNow - d).TotalMilliseconds;

        public static Stream GetResource<T>(string name)
        {
            var asm = typeof(T).Assembly;
            return asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(x => x.EndsWith(name)));
        }

        public static List<string> ReadAllLines(Stream stream)
        {
            string line;
            var lines = new List<string>();

            using var sr = new StreamReader(stream, Encoding.UTF8);
            while ((line = sr.ReadLine()) != null)
                lines.Add(line);

            return lines;
        }

        public static Action<MenuDef> CreateFileMenu(Action<string> onResult, MenuDef returnLevel = null)
        {
            void GenMenu(MenuDef menu, string path, MenuDef parent) {

                if (parent == null)
                {
                    menu.Title = "Select directory";
                    parent = menu.Parent;
                }

                string[] items;

                try
                {
                    if (path == null && OperatingSystem.IsWindows())
                        items = Directory.GetLogicalDrives();
                    else
                    {
                        path ??= "/";
                        var trueDir = Directory.GetLogicalDrives().Contains(path) ? null : Directory.ResolveLinkTarget(path, true);
                        items = Directory.GetDirectories(trueDir?.FullName ?? path);
                    }
                }
                catch (Exception) { return; }

                Array.Sort(items);

                if (path != null)
                    menu.Items.Add(new MenuItem() { Text = "<SELECT CURRENT FOLDER>", OnClick = () => onResult(path), ReturnLevel = parent });

                menu.Items.AddRange(items.Select(x => new MenuItem()
                {
                    Text = path == null ? x : Path.GetFileName(x),
                    Submenu = m => GenMenu(m, x, parent)
                }));
            }

            return x => GenMenu(x, null, returnLevel);
        }
    }


    [JsonSerializable(typeof(string[]))]
    public partial class MySerializerContext : JsonSerializerContext
    {
    }

    public class PriorityScheduler : TaskScheduler
    {
        public static Task RunLowPrioTask(Action a)
            => Task.Factory.StartNew(a, CancellationToken.None, TaskCreationOptions.None, Lowest);

        public static readonly PriorityScheduler Lowest = new(ThreadPriority.Lowest);

        public static Task RunBackgroundThread(Action a, ThreadPriority priority, string name = null)
        {
            var res = new TaskCompletionSource();

            RunBackgroundThreadInternal(() =>
            {
                try
                {
                    a();
                    res.SetResult();
                }
                catch (Exception ex) { res.SetException(ex); }

            }, priority, name);

            return res.Task;
        }

        static Thread RunBackgroundThreadInternal(ThreadStart a, ThreadPriority priority, string name)
        {
            var res = new Thread(a)
            {
                Priority = priority,
                IsBackground = true
            };

            if (name != null)
                res.Name = name;
            res.Start();
            return res;
        }

        private readonly BlockingCollection<Task> _tasks = new (2); // no long queue
        private readonly Thread[] _threads;
        private ThreadPriority _priority;
        private readonly int _maximumConcurrencyLevel;

        public PriorityScheduler(ThreadPriority priority)
        {
            _priority = priority;
            _maximumConcurrencyLevel = Math.Max(2, Environment.ProcessorCount / 3);

            _threads = new Thread[_maximumConcurrencyLevel];
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = RunBackgroundThreadInternal(() =>
                {
                    foreach (Task t in _tasks.GetConsumingEnumerable())
                        base.TryExecuteTask(t);
                }, _priority, $"Scheduler {_priority}: {i}");
                
            }
        }

        public override int MaximumConcurrencyLevel
        {
            get { return _maximumConcurrencyLevel; }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return _tasks;
        }

        protected override void QueueTask(Task task)
        {
            _tasks.Add(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false; // we might not want to execute task that should schedule as high or low priority inline
        }
    }

    class ArrayFilter<T>
    {
        readonly HashSet<int> ToRemove = new();
        private readonly Func<T, bool> filter;
        public bool HasChanges;

        public ArrayFilter(Func<T, bool> filter)
        {
            this.filter = filter;
        }

        public T[] Filter(T[] arr)
        {
            ToRemove.Clear();

            for (int i = 0; i < arr.Length; i++)
            {
                if (!filter(arr[i]))
                    ToRemove.Add(i);
            }

            if (ToRemove.Count > 0)
            {
                HasChanges = true;
                var res = new T[arr.Length - ToRemove.Count];
                int cntr = 0;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (!ToRemove.Contains(i))
                        res[cntr++] = arr[i];
                }

                return res;
            }

            return arr;
        }
       
    }
}
