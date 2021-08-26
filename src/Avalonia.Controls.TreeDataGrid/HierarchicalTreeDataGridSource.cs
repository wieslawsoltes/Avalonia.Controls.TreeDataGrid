﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;

namespace Avalonia.Controls
{
    /// <summary>
    /// A data source for a <see cref="TreeDataGrid"/> which displays a hierarchial tree where each
    /// row may have multiple columns.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    public class HierarchicalTreeDataGridSource<TModel> : ITreeDataGridSource,
        IDisposable,
        IExpanderRowController<TModel>
        where TModel : class
    {
        private IEnumerable<TModel> _items;
        private ItemsSourceViewFix<TModel> _itemsView;
        private IExpanderColumn<TModel>? _expanderColumn;
        private HierarchicalRows<TModel>? _rows;
        private Comparison<TModel>? _comparison;
        private ITreeDataGridSelectionModel? _selection;

        public HierarchicalTreeDataGridSource(TModel item)
            : this(new[] { item })
        {
        }

        public HierarchicalTreeDataGridSource(IEnumerable<TModel> items)
        {
            _items = items;
            _itemsView = ItemsSourceViewFix<TModel>.GetOrCreate(items);
            Columns = new ColumnList<TModel>();
            Columns.CollectionChanged += OnColumnsCollectionChanged;
        }

        public IEnumerable<TModel> Items 
        {
            get => _items;
            set
            {
                if (_items != value)
                {
                    _items = value;
                    _itemsView = ItemsSourceViewFix<TModel>.GetOrCreate(value);
                    _rows?.SetItems(_itemsView);
                }
            }
        }

        public IRows Rows => GetOrCreateRows();
        public ColumnList<TModel> Columns { get; }

        public ITreeDataGridSelectionModel Selection
        {
            get => _selection ??= new HierarchicalTreeDataGridSelectionModel<TModel>(this);
            set
            {
                if (value is null)
                    throw new ArgumentNullException(nameof(value));
                if (_selection is object)
                    throw new InvalidOperationException("Selection model is already set.");
                _selection = value;
            }
        }

        IColumns ITreeDataGridSource.Columns => Columns;

        public event EventHandler<RowEventArgs<HierarchicalRow<TModel>>>? RowExpanding;
        public event EventHandler<RowEventArgs<HierarchicalRow<TModel>>>? RowExpanded;
        public event EventHandler<RowEventArgs<HierarchicalRow<TModel>>>? RowCollapsing;
        public event EventHandler<RowEventArgs<HierarchicalRow<TModel>>>? RowCollapsed;
        public event Action? Sorted;

        public void Dispose() => _rows?.Dispose();
        public void Expand(IndexPath index) => GetOrCreateRows().Expand(index);
        public void Collapse(IndexPath index) => GetOrCreateRows().Collapse(index);

        public void Sort(Comparison<TModel>? comparison)
        {
            _comparison = comparison;
            _rows?.Sort(_comparison);
        }

        public bool SortBy(IColumn? column, ListSortDirection direction, ISelectionModel selection)
        {
            if (column is IColumn<TModel> columnBase &&
                Columns.Contains(columnBase) &&
                columnBase.GetComparison(direction) is Comparison<TModel> comparison)
            {
                Sort(comparison);
                Sorted?.Invoke();
                foreach (var c in Columns)
                    c.SortDirection = c == column ? (ListSortDirection?)direction : null;
                return true;
            }

            return false;
        }

        void IExpanderRowController<TModel>.OnBeginExpandCollapse(IExpanderRow<TModel> row)
        {
            if (row is HierarchicalRow<TModel> r)
            {
                if (!row.IsExpanded)
                    RowExpanding?.Invoke(this, RowEventArgs.Create(r));
                else
                    RowCollapsing?.Invoke(this, RowEventArgs.Create(r));
            }
        }

        void IExpanderRowController<TModel>.OnEndExpandCollapse(IExpanderRow<TModel> row)
        {
            if (row is HierarchicalRow<TModel> r)
            {
                if (row.IsExpanded)
                    RowExpanded?.Invoke(this, RowEventArgs.Create(r));
                else
                    RowCollapsed?.Invoke(this, RowEventArgs.Create(r));
            }
        }

        void IExpanderRowController<TModel>.OnChildCollectionChanged(
            IExpanderRow<TModel> row,
            NotifyCollectionChangedEventArgs e)
        {
        }

        public bool TryGetModelAt(IndexPath index, [NotNullWhen(true)] out TModel? result)
        {
            if (_expanderColumn is null)
                throw new InvalidOperationException("No expander column defined.");

            var items = (IEnumerable<TModel>?)Items;
            var count = index.GetSize();

            for (var depth = 0; depth < count; ++depth)
            {
                var i = index.GetAt(depth);

                if (i < items.Count())
                {
                    var e = items.ElementAt(i);

                    if (depth < count - 1)
                    { 
                        items = _expanderColumn.GetChildModels(e); 
                    }
                    else
                    {
                        result = e;
                        return true;
                    }
                }
                else
                {
                    break;
                }
            }

            result = default;
            return false;
        }

        internal int GetRowIndex(in IndexPath index, int fromRowIndex = 0) =>
            _rows?.GetRowIndex(index, fromRowIndex) ?? -1;

        private HierarchicalRows<TModel> GetOrCreateRows()
        {
            if (_rows is null)
            {
                if (Columns.Count == 0)
                    throw new InvalidOperationException("No columns defined.");
                if (_expanderColumn is null)
                    throw new InvalidOperationException("No expander column defined.");
                _rows = new HierarchicalRows<TModel>(this, _itemsView, _expanderColumn, _comparison);
            }

            return _rows;
        }

        private void OnColumnsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (_expanderColumn is null)
                    {
                        foreach (var i in e.NewItems)
                        {
                            if (i is IExpanderColumn<TModel> expander)
                            {
                                _expanderColumn = expander;
                                break;
                            }
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}