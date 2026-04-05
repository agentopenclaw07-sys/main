using System;
using System.Collections.Generic;
using UnityEngine;

namespace Evolve.Utils
{
    /// <summary>
    /// Generic object pool for Unity GameObjects.
    /// Used by: UI list items, particle effects, damage numbers, entity visuals.
    /// Pre-warms on init, grows dynamically, recycles on return.
    /// </summary>
    public class ObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly Stack<T> _available = new();
        private readonly List<T> _all = new();
        private readonly int _maxSize;

        public int ActiveCount => _all.Count - _available.Count;
        public int TotalCount => _all.Count;

        public ObjectPool(T prefab, Transform parent, int prewarmCount = 10, int maxSize = 500)
        {
            _prefab = prefab;
            _parent = parent;
            _maxSize = maxSize;

            for (int i = 0; i < prewarmCount; i++)
            {
                var obj = Create();
                obj.gameObject.SetActive(false);
                _available.Push(obj);
            }
        }

        public T Get()
        {
            T obj;
            if (_available.Count > 0)
            {
                obj = _available.Pop();
            }
            else if (_all.Count < _maxSize)
            {
                obj = Create();
            }
            else
            {
                return null; // pool exhausted
            }

            obj.gameObject.SetActive(true);
            return obj;
        }

        public void Return(T obj)
        {
            if (obj == null) return;
            obj.gameObject.SetActive(false);
            _available.Push(obj);
        }

        public void ReturnAll()
        {
            foreach (var obj in _all)
            {
                if (obj.gameObject.activeSelf)
                {
                    obj.gameObject.SetActive(false);
                    _available.Push(obj);
                }
            }
        }

        private T Create()
        {
            var obj = UnityEngine.Object.Instantiate(_prefab, _parent);
            _all.Add(obj);
            return obj;
        }
    }

    /// <summary>
    /// Batch processor for large entity collections.
    /// Processes N entities per frame to maintain framerate.
    /// </summary>
    public class BatchProcessor<T>
    {
        private readonly T[] _items;
        private readonly int _count;
        private readonly int _batchSize;
        private int _currentIndex;

        public BatchProcessor(T[] items, int count, int batchSize = 100)
        {
            _items = items;
            _count = count;
            _batchSize = batchSize;
            _currentIndex = 0;
        }

        /// <summary>
        /// Process next batch. Returns true when full cycle is complete.
        /// </summary>
        public bool ProcessBatch(Action<T, int> processor)
        {
            int end = Math.Min(_currentIndex + _batchSize, _count);

            for (int i = _currentIndex; i < end; i++)
            {
                processor(_items[i], i);
            }

            _currentIndex = end;

            if (_currentIndex >= _count)
            {
                _currentIndex = 0;
                return true; // cycle complete
            }

            return false;
        }
    }

    /// <summary>
    /// Throttled update scheduler. Ensures expensive operations don't
    /// all run on the same frame.
    /// </summary>
    public class UpdateScheduler
    {
        private readonly struct ScheduledTask
        {
            public readonly Action Callback;
            public readonly float Interval;
            public float NextRun;

            public ScheduledTask(Action callback, float interval, float offset)
            {
                Callback = callback;
                Interval = interval;
                NextRun = offset;
            }
        }

        private ScheduledTask[] _tasks;
        private int _count;

        public UpdateScheduler(int capacity = 32)
        {
            _tasks = new ScheduledTask[capacity];
        }

        public void Register(Action callback, float intervalSeconds, float offsetSeconds = 0)
        {
            if (_count >= _tasks.Length) Array.Resize(ref _tasks, _tasks.Length * 2);
            _tasks[_count++] = new ScheduledTask(callback, intervalSeconds, offsetSeconds);
        }

        public void Tick(float time)
        {
            for (int i = 0; i < _count; i++)
            {
                if (time >= _tasks[i].NextRun)
                {
                    _tasks[i].Callback();
                    _tasks[i] = new ScheduledTask(
                        _tasks[i].Callback,
                        _tasks[i].Interval,
                        time + _tasks[i].Interval
                    );
                }
            }
        }
    }
}
