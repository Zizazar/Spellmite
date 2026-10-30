using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    /// <summary>Hands background task results back to the editor's main thread.</summary>
    [InitializeOnLoad]
    static class EditorDispatcher
    {
        static readonly ConcurrentQueue<Action> Queue = new();

        static EditorDispatcher()
        {
            // update (not delayCall) also ticks while the editor window is in the background.
            EditorApplication.update += Pump;
        }

        public static void Post(Action action) => Queue.Enqueue(action);

        static void Pump()
        {
            while (Queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
