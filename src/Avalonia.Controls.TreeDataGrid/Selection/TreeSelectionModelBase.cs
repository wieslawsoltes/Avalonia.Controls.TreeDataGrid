﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

#nullable enable

namespace Avalonia.Controls.Selection
{
    public abstract class TreeSelectionModelBase<T> : ITreeSelectionModel, INotifyPropertyChanged
    {
        private TreeSelectionNode<T> _root;
        private bool _singleSelect = true;
        private IndexPath _anchorIndex;
        private IndexPath _selectedIndex;
        private Operation? _operation;
        private TreeSelectedIndexes<T>? _selectedIndexes;
        private TreeSelectedItems<T>? _selectedItems;
        private EventHandler<TreeSelectionModelSelectionChangedEventArgs>? _untypedSelectionChanged;

        protected TreeSelectionModelBase()
        {
            _root = new(this);
        }

        protected TreeSelectionModelBase(IEnumerable source)
            : this()
        {
            Source = source;
        }

        public int Count { get; }

        public bool SingleSelect 
        {
            get => _singleSelect;
            set
            {
                if (_singleSelect != value)
                {
                    if (value == true)
                    {
                        SelectedIndex = _selectedIndex;
                    }

                    _singleSelect = value;

                    RaisePropertyChanged(nameof(SingleSelect));
                }
            }
        }

        public IndexPath SelectedIndex 
        {
            get => _selectedIndex;
            set
            {
                using var update = BatchUpdate();
                Clear();
                Select(value);
            }
        }

        public IReadOnlyList<IndexPath> SelectedIndexes => _selectedIndexes ??= new(this);
        public T? SelectedItem
        {
            get => Source is null || _selectedIndex == default ? default : GetSelectedItemAt(_selectedIndex);
        }

        public IReadOnlyList<T?> SelectedItems => _selectedItems ??= new(this);

        public IndexPath AnchorIndex 
        {
            get => _anchorIndex;
            set => _anchorIndex = value;
        }

        object? ITreeSelectionModel.SelectedItem => SelectedItem;
        IReadOnlyList<object?> ITreeSelectionModel.SelectedItems => _selectedItems ??= new(this);

        IEnumerable? ITreeSelectionModel.Source
        {
            get => Source;
            set => Source = (IEnumerable<T>?)value;
        }

        internal TreeSelectionNode<T> Root => _root;

        protected IEnumerable? Source 
        {
            get => _root.Source;
            set => _root.Source = value;
        }

        public event EventHandler<TreeSelectionModelSelectionChangedEventArgs<T>>? SelectionChanged;
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<TreeSelectionModelIndexesChangedEventArgs>? IndexesChanged;

        event EventHandler<TreeSelectionModelSelectionChangedEventArgs>? ITreeSelectionModel.SelectionChanged
        {
            add => _untypedSelectionChanged += value;
            remove => _untypedSelectionChanged -= value;
        }

        public BatchUpdateOperation BatchUpdate() => new BatchUpdateOperation(this);

        public void BeginBatchUpdate()
        {
            _operation ??= new Operation(this);
            ++_operation.UpdateCount;
        }

        public void EndBatchUpdate()
        {
            if (_operation is null || _operation.UpdateCount == 0)
                throw new InvalidOperationException("No batch update in progress.");
            if (--_operation.UpdateCount == 0)
                CommitOperation(_operation);
        }
        
        public void Clear()
        {
            using var update = BatchUpdate();
            var o = update.Operation;
            _root.Clear(o);
            o.SelectedIndex = default;
        }

        public void Deselect(IndexPath index)
        {
            if (!IsSelected(index))
                return;

            using var update = BatchUpdate();
            var o = update.Operation;

            o.DeselectedRanges ??= new();
            o.SelectedRanges?.Remove(index);
            o.DeselectedRanges.Add(index);

            if (o.DeselectedRanges?.Contains(_selectedIndex) == true)
                o.SelectedIndex = GetFirstSelectedIndex(_root, except: o.DeselectedRanges);
        }

        public bool IsSelected(IndexPath index)
        {
            if (index == default)
                return false;
            var node = GetNode(index.GetParent());
            return IndexRange.Contains(node?.Ranges, index.GetLeaf()!.Value);
        }

        public void Select(IndexPath index)
        {
            if (index == default || !TryGetItemAt(index, out _))
                return;

            using var update = BatchUpdate();
            var o = update.Operation;

            if (SingleSelect)
                Clear();

            o.DeselectedRanges?.Remove(index);

            if (!IsSelected(index))
            {
                o.SelectedRanges ??= new();
                o.SelectedRanges.Add(index);
            }

            if (o.SelectedIndex == default)
                o.SelectedIndex = index;
            o.AnchorIndex = index;
        }

        protected internal abstract IEnumerable<T>? GetChildren(T node);
        
        protected virtual bool TryGetItemAt(IndexPath index, out T? result)
        {
            var items = (IReadOnlyList<T>?)_root.ItemsView;
            var count = index.GetSize();

            for (var i = 0; i < count; ++i)
            {
                if (items is null)
                {
                    result = default;
                    return false;
                }

                var j = index.GetAt(i);

                if (j < items.Count)
                {
                    if (i == count - 1)
                    {
                        result = items[j];
                        return true;
                    }
                    else
                        items = GetChildren(items[j]) as IReadOnlyList<T>;
                }
            }

            result = default;
            return false;
        }

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        internal T GetSelectedItemAt(in IndexPath path)
        {
            if (path == default)
                throw new ArgumentOutOfRangeException();
            if (Source is null)
                throw new InvalidOperationException("Cannot get item from null Source.");

            if (path != default)
            {
                var node = GetNode(path.GetParent());

                if (node is object)
                {
                    return node.ItemsView![path.GetLeaf()!.Value];
                }
            }

            throw new ArgumentOutOfRangeException();
        }

        internal void OnIndexesChanged(IndexPath parentIndex, int shiftIndex, int shiftDelta)
        {
            IndexesChanged?.Invoke(this, new TreeSelectionModelIndexesChangedEventArgs(parentIndex, shiftIndex, shiftDelta));

            if (ShiftIndex(parentIndex, shiftIndex, shiftDelta, ref _selectedIndex))
            {
                RaisePropertyChanged(nameof(SelectedIndex));
            }

            if (ShiftIndex(parentIndex, shiftIndex, shiftDelta, ref _anchorIndex))
            {
                RaisePropertyChanged(nameof(AnchorIndex));
            }
        }

        internal void OnSelectionRemoved(IndexPath parentIndex, int index, int count, IReadOnlyList<T> deselectedItems)
        {
            using var update = BatchUpdate();
            var o = update.Operation;

            if (_selectedIndex != default && (_selectedIndex == parentIndex || parentIndex.IsAncestorOf(_selectedIndex)))
                o.SelectedIndex = default;
            if (_anchorIndex != default && (_anchorIndex == parentIndex || parentIndex.IsAncestorOf(_anchorIndex)))
                o.AnchorIndex = default;

            o.DeselectedItems = deselectedItems;
        }

        private IndexPath GetFirstSelectedIndex(TreeSelectionNode<T> node, IndexRanges? except = null)
        {
            if (node.Ranges.Count > 0)
            {
                var count = IndexRange.GetCount(node.Ranges);
                var index = 0;

                while (index < count)
                {
                    var result = node.Path.CloneWithChildIndex(IndexRange.GetAt(node.Ranges, index++));
                    if (except?.Contains(result) != true)
                        return result;
                }
            }
            
            if (node.Children is object)
            {
                foreach (var child in node.Children)
                {
                    if (child is object)
                    {
                        var i = GetFirstSelectedIndex(child, except);
                        
                        if (i != default)
                            return i;
                    }
                }
            }

            return default;
        }

        private TreeSelectionNode<T>? GetNode(in IndexPath path)
        {
            var depth = path.GetSize();
            TreeSelectionNode<T>? node = _root;

            for (var i = 0; i < depth; ++i)
            {
                node = node!.GetChild(path.GetAt(i));
                if (node is null)
                    break;
            }

            return node;
        }

        private TreeSelectionNode<T>? GetOrCreateNode(in IndexPath path)
        {
            var depth = path.GetSize();
            TreeSelectionNode<T>? node = _root;

            for (var i = 0; i < depth; ++i)
            {
                node = node!.GetOrCreateChild(path.GetAt(i));
                if (node is null)
                    break;
            }

            return node;
        }

        private void CommitOperation(Operation operation)
        {
            var oldAnchorIndex = _anchorIndex;
            var oldSelectedIndex = _selectedIndex;
            var indexesChanged = false;

            _selectedIndex = operation.SelectedIndex;
            _anchorIndex = operation.AnchorIndex;

            if (operation.SelectedRanges is object)
            {
                indexesChanged |= CommitSelect(operation.SelectedRanges) > 0;
            }

            if (operation.DeselectedRanges is object)
            {
                indexesChanged |= CommitDeselect(operation.DeselectedRanges) > 0;
            }

            if ((SelectionChanged is object || _untypedSelectionChanged is object) &&
                (operation.DeselectedRanges?.Count > 0 ||
                 operation.SelectedRanges?.Count > 0 ||
                 operation.DeselectedItems is object))
            {
                var deselectedIndexes = operation.DeselectedRanges;
                var selectedIndexes = operation.SelectedRanges;
                var deselectedItems = operation.DeselectedItems ??
                    TreeSelectionChangedItems<T>.Create(this, deselectedIndexes);

                var e = new TreeSelectionModelSelectionChangedEventArgs<T>(
                    deselectedIndexes,
                    selectedIndexes,
                    deselectedItems,
                    TreeSelectionChangedItems<T>.Create(this, selectedIndexes));
                SelectionChanged?.Invoke(this, e);
                _untypedSelectionChanged?.Invoke(this, e);
            }

            if (oldSelectedIndex != _selectedIndex)
            {
                indexesChanged = true;
                RaisePropertyChanged(nameof(SelectedIndex));
                RaisePropertyChanged(nameof(SelectedItem));
            }

            if (oldAnchorIndex != _anchorIndex)
                RaisePropertyChanged(nameof(AnchorIndex));

            if (indexesChanged)
            {
                RaisePropertyChanged(nameof(SelectedIndexes));
                RaisePropertyChanged(nameof(SelectedItems));
            }

            _operation = null;
        }

        private int CommitSelect(IndexRanges selectedRanges)
        {
            var result = 0;

            foreach (var (parent, ranges) in selectedRanges.Ranges)
            {
                var node = GetOrCreateNode(parent);

                if (node is object)
                {
                    foreach (var range in ranges)
                        result += node.CommitSelect(range);
                }
            }

            return result;
        }

        private int CommitDeselect(IndexRanges selectedRanges)
        {
            var result = 0;

            foreach (var (parent, ranges) in selectedRanges.Ranges)
            {
                var node = GetOrCreateNode(parent);

                if (node is object)
                {
                    foreach (var range in ranges)
                        result += node.CommitDeselect(range);
                }
            }

            return result;
        }

        internal static bool ShiftIndex(IndexPath parentIndex, int shiftIndex, int shiftDelta, ref IndexPath path)
        {
            if (parentIndex.IsAncestorOf(path) && path.GetAt(parentIndex.GetSize()) >= shiftIndex)
            {
                var indexes = path.ToArray();
                indexes[parentIndex.GetSize()] += shiftDelta;
                path = new IndexPath(indexes);
                return true;
            }

            return false;
        }

        public struct BatchUpdateOperation : IDisposable
        {
            private readonly TreeSelectionModelBase<T> _owner;
            private bool _isDisposed;

            public BatchUpdateOperation(TreeSelectionModelBase<T> owner)
            {
                _owner = owner;
                _isDisposed = false;
                owner.BeginBatchUpdate();
            }

            internal Operation Operation => _owner._operation!;

            public void Dispose()
            {
                if (!_isDisposed)
                {
                    _owner?.EndBatchUpdate();
                    _isDisposed = true;
                }
            }
        }

        internal class Operation
        {
            public Operation(TreeSelectionModelBase<T> owner)
            {
                AnchorIndex = owner.AnchorIndex;
                SelectedIndex = owner.SelectedIndex;
            }

            public int UpdateCount { get; set; }
            public bool IsSourceUpdate { get; set; }
            public IndexPath AnchorIndex { get; set; }
            public IndexPath SelectedIndex { get; set; }
            public IndexRanges? SelectedRanges { get; set; }
            public IndexRanges? DeselectedRanges { get; set; }
            public IReadOnlyList<T>? DeselectedItems { get; set; }
        }
    }
}