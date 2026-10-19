using System.Collections.Concurrent;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Formwerk.Tests.App;

/// <summary>
/// A test that runs like the application does: on one thread with a message pump, so that what a view model continues with after
/// background work (<c>await ... ConfigureAwait(true)</c>) happens on the thread of the test, one thing at a time, and only when
/// the test waits. xUnit's own context (<c>AsyncTestSyncContext</c>) moves such continuations to other threads, which made tests
/// race against the view model's own background work.
/// </summary>
[XunitTestCaseDiscoverer("Formwerk.Tests.App.UiFactDiscoverer", "Formwerk.Tests.App")]
public sealed class UiFactAttribute : FactAttribute
{
}

public sealed class UiFactDiscoverer(IMessageSink diagnosticMessageSink) : IXunitTestCaseDiscoverer
{
    public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
    {
        yield return new UiTestCase(
            diagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
    }
}

public sealed class UiTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes", error: true)]
    public UiTestCase()
    {
    }

    public UiTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
    }

    public override Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments,
        ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
    {
        var result = new TaskCompletionSource<RunSummary>();
        var thread = new Thread(() =>
        {
            var context = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var run = base.RunAsync(diagnosticMessageSink, messageBus, constructorArguments, aggregator, cancellationTokenSource);
                context.RunUntilCompleted(run);
                result.SetResult(run.GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        })
        {
            Name = "UiFact",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA); // like the UI thread: WPF controls can be created in these tests
        thread.Start();
        return result.Task;
    }

    /// <summary>Runs what is posted to it, one callback at a time, on the thread that pumps it.</summary>
    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly int _threadId = Environment.CurrentManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // The test is over (background work of a view model finished late): nobody is waiting for it any more.
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == _threadId)
            {
                d(state);
                return;
            }

            using var done = new ManualResetEventSlim();
            Exception? error = null;
            Post(_ =>
            {
                try { d(state); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            }, null);
            done.Wait();
            if (error is not null) throw error;
        }

        public override SynchronizationContext CreateCopy() => this;

        public void RunUntilCompleted(Task task)
        {
            task.ContinueWith(_ => _queue.CompleteAdding(), TaskScheduler.Default);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        }
    }
}
