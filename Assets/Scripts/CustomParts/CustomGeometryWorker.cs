using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Protobot.CustomParts {
    // One running job and one replaceable request. Dragging a handle cannot queue
    // an unbounded trail of obsolete meshes or apply a stale result after Cancel.
    public sealed class CustomGeometryWorker : IDisposable {
        private sealed class Request {
            public CustomPartDefinition snapshot;
            public string key;
            public long version;
            public Action<CustomPartMeshBuilder.GeometryData> apply;
        }
        private Request pending, active;
        private Task<CustomPartMeshBuilder.GeometryData> task;
        private CancellationTokenSource cancellation;
        private long version;
        private bool disposed;
        public int StartedCount { get; private set; }
        public int AppliedCount { get; private set; }
        public int DiscardedCount { get; private set; }
        public bool Busy => task != null || pending != null;

        public void RequestGeometry(CustomPartDefinition definition, string key, Action<CustomPartMeshBuilder.GeometryData> apply) {
            if (disposed) return;
            Cancel();
            pending = new Request { snapshot = definition.CloneDeep(), key = key, version = version, apply = apply };
            Tick();
        }
        public void Cancel() {
            version++; pending = null;
            if (cancellation != null && !cancellation.IsCancellationRequested) cancellation.Cancel();
        }
        public void Tick() {
            if (disposed) return;
            if (task != null && task.IsCompleted) {
                var finished = task; var request = active;
                task = null; active = null;
                cancellation.Dispose(); cancellation = null;
                if (finished.IsFaulted) {
                    var error = finished.Exception.GetBaseException(); // Always observe faults.
                    if (request.version == version && !(error is OperationCanceledException)) {
                        Debug.LogWarning("Custom geometry compilation failed: " + error.Message);
                        request.apply?.Invoke(new CustomPartMeshBuilder.GeometryData { Valid = false, Key = request.key });
                    }
                    DiscardedCount++;
                } else if (!finished.IsCanceled && request.version == version) {
                    AppliedCount++; request.apply?.Invoke(finished.Result);
                } else DiscardedCount++;
            }
            if (task != null || pending == null) return;
            active = pending; pending = null;
            cancellation = new CancellationTokenSource();
            var snapshot = active.snapshot; var key = active.key; var token = cancellation.Token;
            StartedCount++;
            task = Task.Run(() => CustomPartMeshBuilder.Compile(snapshot, key, token), token);
        }
        public void Dispose() {
            if (disposed) return;
            Cancel(); disposed = true;
            if (task != null) {
                var source = cancellation;
                task.ContinueWith(finished => { if (finished.IsFaulted) { var observed = finished.Exception; } source.Dispose(); }, TaskScheduler.Default);
            } else cancellation?.Dispose();
            task = null; active = pending = null; cancellation = null;
        }
    }
}
