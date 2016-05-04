﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AltLinq;

namespace AqlaSerializer
{
    internal class NetObjectKeyPositionsList
    {
        List<int> _keyToPosition = new List<int>();
        
        public void SetPosition(int key, int position)
        {
            if (position < 0 || (key > 0 && position == 0)) throw new ArgumentOutOfRangeException(nameof(position));
            while (_keyToPosition.Count - 1 < key)
                _keyToPosition.Add(0);
            _keyToPosition[key] = position;
        }

        public int GetPosition(int key)
        {
            if (_keyToPosition.Count - 1 < key) ThrowNotFound(key);
            var r = _keyToPosition[key];
            if (r == 0 && key > 0) ThrowNotFound(key);
            return r;
        }

        int _exportKnownCount;
        int _previousExportedPosition;
        int _importKnownCount;
        int _previousImportedPosition;

        public int[] ExportNew()
        {
            var arr = _keyToPosition.Skip(_exportKnownCount).ToArray();
            if (arr.Length == 0) return arr;
            _exportKnownCount = _keyToPosition.Count;
            arr[0] -= _previousExportedPosition;
            for (int i = arr.Length - 1; i > 0; i--)
            {
                arr[i] -= arr[i - 1];
                Helpers.DebugAssert(arr[i] > 0, "arr[i] > 0");
            }
            _previousExportedPosition = arr[arr.Length - 1];
            return arr;
        }

        bool _importingLock;

        public void EnterImportingLock()
        {
            if (_importingLock)throw new ProtoException("An attempt to enter NetObjectKeyPositionsList importing lock when already acquired");
            _importingLock = true;
        }

        public void ReleaseImportingLock()
        {
            if (!_importingLock) throw new ProtoException("An attempt to release NetObjectKeyPositionsList importing lock when not acquired");
            _importingLock = false;
        }
        
        public void ImportNext(int[] arr)
        {
            int acc = _previousImportedPosition;
            for (int i = 0; i < arr.Length; i++)
            {
                acc += arr[i];
                SetPosition(_importKnownCount++, acc);
            }
            _previousImportedPosition = acc;
        }

        public void Reset()
        {
            _keyToPosition.Clear();
            _previousImportedPosition = _previousExportedPosition = _importKnownCount = _exportKnownCount = 0;
            _importingLock = false;
        }

        static void ThrowNotFound(int key)
        {
            throw new KeyNotFoundException(nameof(NetObjectKeyPositionsList) + " can't find a key: " + key + ", try to set TypeModel.EnableVersioningSeeking = true");
        }

        public NetObjectKeyPositionsList Clone()
        {
            var r = (NetObjectKeyPositionsList)MemberwiseClone();
            r._keyToPosition = new List<int>(_keyToPosition);
            return r;
        }
    }
}