using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace GameJam.TeamTools
{
    /// <summary>Hands background task results back to the editor's main thread.</summary>
    [InitializeOnLoad]
    public static class MainThread
    {
        static readonly ConcurrentQueue<Action> Queue = new();

        static MainThread()
        {
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
