using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Models;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using SS14.Launcher.Localization;
using SS14.Launcher.Models.ServerStatus;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public class ServerListTabViewModel : MainWindowTabViewModel
{
    private readonly LocalizationManager _loc = LocalizationManager.Instance;
    private readonly MainWindowViewModel _windowVm;
    private readonly ServerListCache _serverListCache;

    private readonly ObservableCollection<ServerEntryViewModel> _searchedServers = [];
    public ServerTreeDataGridSource<ServerEntryViewModel> SearchedServers { get; }

    private string? _searchString;

    public override string Name => _loc.GetString("tab-servers-title");

    public string? SearchString
    {
        get => _searchString;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchString, value);
            UpdateSearchedList();
        }
    }

    public bool ListTextVisible => _serverListCache.Status != RefreshListStatus.Updated;
    public bool SpinnerVisible => _serverListCache.Status < RefreshListStatus.Updated;

    public string ListText
    {
        get
        {
            var status = _serverListCache.Status;
            switch (status)
            {
                case RefreshListStatus.Error:
                    return _loc.GetString("tab-servers-list-status-error");
                case RefreshListStatus.PartialError:
                    return _loc.GetString("tab-servers-list-status-partial-error");
                case RefreshListStatus.UpdatingMaster:
                    return _loc.GetString("tab-servers-list-status-updating-master");
                case RefreshListStatus.NotUpdated:
                    return "";
                case RefreshListStatus.Updated:
                default:
                    if (_searchedServers.Count == 0 && _serverListCache.AllServers.Count != 0)
                        // TODO: Actually make this show up or just remove it entirely
                        return _loc.GetString("tab-servers-list-status-none-filtered");

                    return _serverListCache.AllServers.Count == 0 ? _loc.GetString("tab-servers-list-status-none") : "";
            }
        }
    }

    [Reactive] public bool FiltersVisible { get; set; }

    public ServerListFiltersViewModel Filters { get; }

    public ServerListTabViewModel(MainWindowViewModel windowVm)
    {
        Filters = new ServerListFiltersViewModel(windowVm.Cfg, _loc);
        Filters.FiltersUpdated += FiltersOnFiltersUpdated;

        _windowVm = windowVm;
        _serverListCache = Locator.Current.GetRequiredService<ServerListCache>();

        _serverListCache.AllServers.CollectionChanged += ServerListUpdated;

        _serverListCache.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(ServerListCache.Status):
                    this.RaisePropertyChanged(nameof(ListText));
                    this.RaisePropertyChanged(nameof(ListTextVisible));
                    this.RaisePropertyChanged(nameof(SpinnerVisible));
                    break;
            }
        };

        SearchedServers = new ServerTreeDataGridSource<ServerEntryViewModel>(_searchedServers)
        {
            Columns =
            {
                // new TemplateColumn<ServerEntryViewModel>("", new FuncDataTemplate<ServerEntryViewModel>((a, e) =>
                //         new Button
                //         {
                //             [!Button.IsEnabled] = new Binding(nameof(ServerEntryViewModel.IsOnline)),
                //             Content = "{loc:Loc server-entry-connect}",
                //         }
                //     //Command="{Binding ConnectPressed}" />
                // )),
                new ServerExpanderColumn<ServerEntryViewModel>(
                    inner:
                    new TextColumn<ServerEntryViewModel, string>("Server Name", x => x.Name)),
                new TextColumn<ServerEntryViewModel, string>("Players", x => x.ServerStatusString),
            },
        };
    }

    public class ServerExpanderColumn<TModel> : NotifyingBase, IColumn<TModel>, IUpdateColumnLayout where TModel : class
    {
        private readonly IColumn<TModel> _inner;
        private double _actualWidth;

        public ServerExpanderColumn(IColumn<TModel> inner)
        {
            _inner = inner;
            _inner.PropertyChanged += OnInnerPropertyChanged;
            _actualWidth = inner.ActualWidth;
        }

        public double ActualWidth
        {
            get => _actualWidth;
            private set => RaiseAndSetIfChanged(ref _actualWidth, value);
        }

        public bool? CanUserResize => _inner.CanUserResize;
        public object? Header => _inner.Header;

        public ListSortDirection? SortDirection
        {
            get => _inner.SortDirection;
            set => _inner.SortDirection = value;
        }

        public object? Tag
        {
            get => _inner.Tag;
            set => _inner.Tag = value;
        }

        public GridLength Width => _inner.Width;
        double IUpdateColumnLayout.MinActualWidth => ((IUpdateColumnLayout)_inner).MinActualWidth;
        double IUpdateColumnLayout.MaxActualWidth => ((IUpdateColumnLayout)_inner).MaxActualWidth;
        bool IUpdateColumnLayout.StarWidthWasConstrained => ((IUpdateColumnLayout)_inner).StarWidthWasConstrained;

        public ICell CreateCell(IRow<TModel> row)
        {
            if (row is not ServerRow<TModel> r)
                throw new NotSupportedException();

            var showExpander = new MyShowExpanderObservable( );
            return new ExpanderCell<TModel>(_inner.CreateCell(r), r, showExpander, null);
        }

        public Comparison<TModel?>? GetComparison(ListSortDirection direction)
        {
            return _inner.GetComparison(direction);
        }

        double IUpdateColumnLayout.CellMeasured(double width, int rowIndex)
        {
            return ((IUpdateColumnLayout)_inner).CellMeasured(width, rowIndex);
        }

        bool IUpdateColumnLayout.CommitActualWidth()
        {
            var result = ((IUpdateColumnLayout)_inner).CommitActualWidth();
            ActualWidth = _inner.ActualWidth;
            return result;
        }

        void IUpdateColumnLayout.CalculateStarWidth(double availableWidth, double totalStars)
        {
            ((IUpdateColumnLayout)_inner).CalculateStarWidth(availableWidth, totalStars);
            ActualWidth = _inner.ActualWidth;
        }

        void IUpdateColumnLayout.SetWidth(GridLength width) => SetWidth(width);

        private void OnInnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CanUserResize) or nameof(Header) or nameof(SortDirection) or nameof(Width))
                RaisePropertyChanged(e);
        }

        private void SetWidth(GridLength width)
        {
            ((IUpdateColumnLayout)_inner).SetWidth(width);

            if (width.IsAbsolute)
                ActualWidth = width.Value;
        }
    }

    public class ServerTreeDataGridSource<TModel>
        : NotifyingBase,
            ITreeDataGridSource<TModel>
        where TModel : class
    {
        private IEnumerable<TModel> _items;
        private TreeDataGridItemsSourceView<TModel> _itemsView;
        private ServerExpanderColumn<TModel>? _expanderColumn;
        private ServerRows<TModel>? _rows;
        private Comparison<TModel>? _comparison;
        private ITreeDataGridSelection? _selection;
        private bool _isSelectionSet;

        public ServerTreeDataGridSource(IEnumerable<TModel> items)
        {
            _items = items;
            _itemsView = TreeDataGridItemsSourceView<TModel>.GetOrCreate(items);
            Columns = [];
            Columns.CollectionChanged += OnColumnsCollectionChanged;
        }

        public IEnumerable<TModel> Items
        {
            get => _items;
            set
            {
                if (Equals(_items, value))
                    return;
                _items = value;
                _itemsView = TreeDataGridItemsSourceView<TModel>.GetOrCreate(value);
                _rows?.SetItems(_itemsView);
                if (_selection is not null)
                    _selection.Source = value;
            }
        }

        public IRows Rows => GetOrCreateRows();
        public ColumnList<TModel> Columns { get; }

        public ITreeDataGridSelection? Selection
        {
            get
            {
                if (_selection == null && !_isSelectionSet)
                    _selection = new TreeDataGridRowSelectionModel<TModel>(this);
                return _selection;
            }
            set
            {
                if (_selection == value)
                    return;
                if (!Equals(value?.Source, _items))
                    throw new InvalidOperationException("Selection source must be set to Items.");
                _selection = value;
                _isSelectionSet = true;
                RaisePropertyChanged();
            }
        }

        IEnumerable<object> ITreeDataGridSource.Items => Items;

        public bool IsHierarchical => true;
        public bool IsSorted => _comparison is not null;

        IColumns ITreeDataGridSource.Columns => Columns;

        public event Action? Sorted;

        private void Sort(Comparison<TModel>? comparison)
        {
            _comparison = comparison;
            _rows?.Sort(_comparison);
        }

        public void DragDropRows(ITreeDataGridSource source, IEnumerable<IndexPath> indexes, IndexPath targetIndex,
            TreeDataGridRowDropPosition position, DragDropEffects effects)
        {
            throw new NotImplementedException();
        }

        IEnumerable<object> ITreeDataGridSource.GetModelChildren(object model)
        {
            _ = _expanderColumn ?? throw new InvalidOperationException("No expander column defined.");
            return [(TModel)model];
        }

        public bool SortBy(IColumn? column, ListSortDirection direction)
        {
            if (column is not IColumn<TModel> columnBase ||
                !Columns.Contains(columnBase) ||
                columnBase.GetComparison(direction) is not { } comparison)
                return false;
            Sort(comparison);
            Sorted?.Invoke();
            foreach (var c in Columns)
                c.SortDirection = c == column ? direction : null;

            return true;
        }

        private ServerRows<TModel> GetOrCreateRows()
        {
            if (_rows is not null)
                return _rows;
            if (Columns.Count == 0)
                throw new InvalidOperationException("No columns defined.");
            if (_expanderColumn is null)
                throw new InvalidOperationException("No expander column defined.");
            _rows = new ServerRows<TModel>(_itemsView, _expanderColumn, _comparison);

            return _rows;
        }

        private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    HandleAdd(e.NewItems);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    HandleRemoveReplaceOrMove(e.OldItems, "removed");
                    break;

                case NotifyCollectionChangedAction.Replace:
                    HandleRemoveReplaceOrMove(e.NewItems, "replaced");
                    break;

                case NotifyCollectionChangedAction.Move:
                    HandleRemoveReplaceOrMove(e.NewItems, "moved");
                    break;

                case NotifyCollectionChangedAction.Reset:
                    if (_expanderColumn is not null)
                    {
                        throw new InvalidOperationException("The expander column cannot be removed by a reset.");
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void HandleAdd(IList? newItems)
        {
            if (newItems is null)
                return;
            foreach (var i in newItems)
            {
                if (i is not ServerExpanderColumn<TModel> expander)
                    continue;
                if (_expanderColumn is not null)
                {
                    throw new InvalidOperationException("Only one expander column is allowed.");
                }

                _expanderColumn = expander;
                break;
            }
        }

        private void HandleRemoveReplaceOrMove(IList? items, string action)
        {
            if (items is null)
                return;
            foreach (var i in items)
            {
                if (i is ServerExpanderColumn<TModel> && _expanderColumn is not null)
                {
                    throw new InvalidOperationException($"The expander column cannot be {action}.");
                }
            }
        }
    }

    private abstract class MySingleSubscriberObservableBase<T> : IObservable<T>, IDisposable
    {
        private IObserver<T>? _observer;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _ = observer ?? throw new ArgumentNullException(nameof(observer));
            Dispatcher.UIThread.VerifyAccess();

            if (_observer != null)
            {
                throw new InvalidOperationException("The observable can only be subscribed once.");
            }

            _observer = observer;
            Subscribed();

            return this;
        }

        public virtual void Dispose()
        {
            Unsubscribed();
            _observer = null;
        }

        protected abstract void Unsubscribed();

        protected void PublishNext(T value)
        {
            _observer?.OnNext(value);
        }

        protected abstract void Subscribed();
    }

    private class MyShowExpanderObservable : MySingleSubscriberObservableBase<bool>
    {
        protected override void Subscribed()
        {
        }

        protected override void Unsubscribed()
        {
        }
    }

    public class ServerRows<TModel> : ReadOnlyListBase<ServerRow<TModel>>, IRows where TModel : class
    {
        private readonly RootRows _roots;
        private readonly ServerExpanderColumn<TModel> _expanderColumn;
        private readonly List<ServerRow<TModel>> _flattenedRows;
        private Comparison<TModel>? _comparison;
        private bool _ignoreCollectionChanges;

        public ServerRows(TreeDataGridItemsSourceView<TModel> items,
            ServerExpanderColumn<TModel> expanderColumn,
            Comparison<TModel>? comparison)
        {
            _flattenedRows = [];
            _roots = new RootRows(this, items, comparison);
            _roots.CollectionChanged += (_, _) => OnCollectionChanged(default);
            _expanderColumn = expanderColumn;
            _comparison = comparison;
            InitializeRows();
        }

        public override ServerRow<TModel> this[int index] => _flattenedRows[index];
        IRow IReadOnlyList<IRow>.this[int index] => _flattenedRows[index];
        public override int Count => _flattenedRows.Count;

        public (int index, double y) GetRowAt(double y)
        {
            return MathUtilities.IsZero(y) ? (0, 0) : (-1, -1);
        }

        public ICell RealizeCell(IColumn column, int columnIndex, int rowIndex)
        {
            if (column is IColumn<TModel> c)
                return c.CreateCell(this[rowIndex]);
            throw new InvalidOperationException("Invalid column.");
        }

        public void SetItems(TreeDataGridItemsSourceView<TModel> items)
        {
            _ignoreCollectionChanges = true;

            try
            {
                _roots.SetItems(items);
            }
            finally
            {
                _ignoreCollectionChanges = false;
            }

            _flattenedRows.Clear();
            InitializeRows();
            CollectionChanged?.Invoke(this, (NotifyCollectionChangedEventArgs)new(NotifyCollectionChangedAction.Reset));
        }

        public void Sort(Comparison<TModel>? comparison)
        {
            _comparison = comparison;
            _roots.Sort(comparison);
            _flattenedRows.Clear();
            InitializeRows();
            CollectionChanged?.Invoke(this, (NotifyCollectionChangedEventArgs)new(NotifyCollectionChangedAction.Reset));
        }

        public void UnrealizeCell(ICell cell, int rowIndex, int columnIndex)
        {
            (cell as IDisposable)?.Dispose();
        }

        public int ModelIndexToRowIndex(IndexPath modelIndex)
        {
            if (modelIndex == default)
                return -1;

            for (var i = 0; i < _flattenedRows.Count; ++i)
            {
                if (_flattenedRows[i].ModelIndexPath == modelIndex)
                    return i;
            }

            return -1;
        }

        public IndexPath RowIndexToModelIndex(int rowIndex)
        {
            if (rowIndex >= 0 && rowIndex < _flattenedRows.Count)
                return _flattenedRows[rowIndex].ModelIndexPath;
            return default;
        }

        public override IEnumerator<ServerRow<TModel>> GetEnumerator() => _flattenedRows.GetEnumerator();
        IEnumerator<IRow> IEnumerable<IRow>.GetEnumerator() => _flattenedRows.GetEnumerator();

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public void OnChildCollectionChanged(IExpanderRow<TModel> row)
        {
            if (_ignoreCollectionChanges)
                return;

            if (row is ServerRow<TModel> h)
                OnCollectionChanged(h.ModelIndexPath);
            else
                throw new NotSupportedException("Unexpected row type.");
        }

        private bool TryGetRowIndex(in IndexPath modelIndex, out int rowIndex, int fromRowIndex = 0)
        {
            if (modelIndex.Count == 0)
            {
                rowIndex = -1;
                return true;
            }

            for (var i = fromRowIndex; i < _flattenedRows.Count; ++i)
            {
                if (modelIndex != _flattenedRows[i].ModelIndexPath)
                    continue;
                rowIndex = i;
                return true;
            }

            rowIndex = -1;
            return false;
        }

        private void InitializeRows()
        {
            var i = 0;

            foreach (var model in _roots)
            {
                i += AddRowsAndDescendants(i, model);
            }
        }

        private int AddRowsAndDescendants(int index, ServerRow<TModel> row)
        {
            var i = index;
            _flattenedRows.Insert(i++, row);

            if (row.Children == null)
                return i - index;
            foreach (var childRow in row.Children)
            {
                i += AddRowsAndDescendants(i, childRow);
            }

            return i - index;
        }

        private void OnCollectionChanged( in IndexPath parentIndex)
        {
            if (_ignoreCollectionChanges)
                return;

            if (!TryGetRowIndex(parentIndex, out var parentRowIndex))
                return;

            var children = parentRowIndex >= 0 ? _flattenedRows[parentRowIndex].Children : _roots;
            var count = GetDescendentRowCount(parentRowIndex);
            Remove(parentRowIndex + 1, count, true);
            Add(parentRowIndex + 1, children, true);

            return;

            void Add(int index, IEnumerable? items, bool raise)
            {
                if (items is null)
                    return;

                var start = index;

                foreach (ServerRow<TModel> row in items)
                {
                    index += AddRowsAndDescendants(index, row);
                }

                if (raise && index > start)
                {
                    CollectionChanged?.Invoke(
                        this,
                        new NotifyCollectionChangedEventArgs(
                            NotifyCollectionChangedAction.Add,
                            new MyListSpan(_flattenedRows, start, index - start),
                            start));
                }
            }

            void Remove(int index, int count, bool raise)
            {
                if (count == 0)
                    return;

                var oldItems = raise && CollectionChanged is not null ? new ServerRow<TModel>[count] : null;

                for (var i = 0; i < count; ++i)
                {
                    var row = _flattenedRows[i + index];
                    if (oldItems is not null)
                        oldItems[i] = row;
                }

                _flattenedRows.RemoveRange(index, count);

                if (oldItems is not null)
                {
                    CollectionChanged!(
                        this,
                        new NotifyCollectionChangedEventArgs(
                            NotifyCollectionChangedAction.Remove,
                            oldItems,
                            index));
                }
            }

            int GetDescendentRowCount(int rowIndex)
            {
                if (rowIndex == -1)
                    return _flattenedRows.Count;

                var row = _flattenedRows[rowIndex];
                var depth = row.ModelIndexPath.Count;
                var i = rowIndex + 1;

                while (i < _flattenedRows.Count && _flattenedRows[i].ModelIndexPath.Count > depth)
                    ++i;

                return i - (rowIndex + 1);
            }
        }

        private class RootRows (
            ServerRows<TModel> _owner,
            TreeDataGridItemsSourceView<TModel> items,
            Comparison<TModel>? comparison)
            : SortableRowsBase<TModel, ServerRow<TModel>>(items, comparison)
        {
            protected override ServerRow<TModel> CreateRow(int modelIndex, TModel model)
            {
                return new ServerRow<TModel>(
                    _owner,
                    _owner._expanderColumn,
                    new IndexPath(modelIndex),
                    model,
                    _owner._comparison);
            }
        }
    }

    public class ServerRow<TModel> : NotifyingBase,
        IExpanderRow<TModel>,
        IIndentedRow,
        IModelIndexableRow,
        IDisposable where TModel : class
    {
        private readonly ServerRows<TModel> _controller;
        private readonly ServerExpanderColumn<TModel> _expanderColumn;
        private readonly Comparison<TModel>? _comparison;
        private TModel? _childModel;
        private ChildRows? _childRows;
        private bool _isExpanded;

        public ServerRow(
            ServerRows<TModel> controller,
            ServerExpanderColumn<TModel> expanderColumn,
            IndexPath modelIndex,
            TModel model,
            Comparison<TModel>? comparison)
        {
            if (modelIndex.Count == 0)
                throw new ArgumentException("Invalid model index");

            _controller = controller;
            _expanderColumn = expanderColumn;
            _comparison = comparison;
            ModelIndexPath = modelIndex;
            Model = model;
        }

        /// <summary>
        /// Gets the row's visible child rows.
        /// </summary>
        public IReadOnlyList<ServerRow<TModel>>? Children => _isExpanded ? _childRows : null;

        /// <summary>
        /// Gets the index of the model relative to its parent.
        /// </summary>
        /// <remarks>
        /// To retrieve the index path to the model from the root data source, see
        /// <see cref="ModelIndexPath"/>.
        /// </remarks>
        public int ModelIndex => ModelIndexPath[^1];

        /// <summary>
        /// Gets the index path of the model in the data source.
        /// </summary>
        public IndexPath ModelIndexPath { get; private set; }

        public object Header => ModelIndexPath;
        public int Indent => ModelIndexPath.Count - 1;
        public TModel Model { get; }

        public GridLength Height
        {
            get => GridLength.Auto;
            set { }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                if (value)
                {
                    _childModel = Model;
                    _childRows = new ChildRows(this, Model, _comparison);
                }

                _isExpanded = value;
                _controller.OnChildCollectionChanged(this);
                RaisePropertyChanged();
            }
        }

        public bool ShowExpander => true;

        public void Dispose()
        {
        }

        public void UpdateModelIndex(int delta)
        {
            ModelIndexPath = ModelIndexPath[..^1].Append(ModelIndexPath[^1] + delta);

            if (_childRows is null)
                return;

            var childCount = _childRows.Count;

            for (var i = 0; i < childCount; ++i)
                _childRows[i].UpdateParentModelIndex(ModelIndexPath);
        }

        private void UpdateParentModelIndex(IndexPath parentIndex)
        {
            ModelIndexPath = parentIndex.Append(ModelIndex);

            if (_childRows is null)
                return;

            var childCount = _childRows.Count;

            for (var i = 0; i < childCount; ++i)
                _childRows[i].UpdateParentModelIndex(ModelIndexPath);
        }

        void IExpanderRow<TModel>.UpdateShowExpander(IExpanderCell cell, bool value)
        {
        }

        private class ChildRows (
            ServerRow<TModel> owner,
            TModel items,
            Comparison<TModel>? comparison)
            : SortableRowsBase<TModel, ServerRow<TModel>>(new TreeDataGridItemsSourceView<TModel>([items]), comparison)
        {
            protected override ServerRow<TModel> CreateRow(int modelIndex, TModel model)
            {
                return new ServerRow<TModel>(
                    owner._controller,
                    owner._expanderColumn,
                    owner.ModelIndexPath.Append(modelIndex),
                    model,
                    owner._comparison);
            }
        }
    }

    private class MyListSpan (IList items, int index, int count) : IList
    {
        public object? this[int index1]
        {
            get
            {
                if (index1 >= count)
                    throw new ArgumentOutOfRangeException();
                return items[index + index1];
            }
            set => throw new NotSupportedException();
        }

        bool IList.IsFixedSize => true;
        bool IList.IsReadOnly => true;
        int ICollection.Count => count;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;

        public IEnumerator GetEnumerator()
        {
            for (var i = 0; i < count; ++i)
                yield return items[index + i];
        }

        int IList.Add(object? value) => throw new NotSupportedException();
        void IList.Clear() => throw new NotSupportedException();
        bool IList.Contains(object? value) => throw new NotSupportedException();
        void ICollection.CopyTo(Array array, int i) => throw new NotSupportedException();
        int IList.IndexOf(object? value) => throw new NotSupportedException();
        void IList.Insert(int i, object? value) => throw new NotSupportedException();
        void IList.Remove(object? value) => throw new NotSupportedException();
        void IList.RemoveAt(int i) => throw new NotSupportedException();
    }

    private void FiltersOnFiltersUpdated()
    {
        UpdateSearchedList();
    }

    public override void Selected()
    {
        _serverListCache.RequestInitialUpdate();
    }

    public void RefreshPressed()
    {
        _serverListCache.RequestRefresh();
    }

    private void ServerListUpdated(object? sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        Filters.UpdatePresentFilters(_serverListCache.AllServers);

        UpdateSearchedList();
    }

    private void UpdateSearchedList()
    {
        var sortList = new List<ServerStatusData>();

        foreach (var server in _serverListCache.AllServers)
        {
            if (!DoesSearchMatch(server))
                continue;

            sortList.Add(server);
        }

        Filters.ApplyFilters(sortList);

        sortList.Sort(ServerSortComparer.Instance);

        _searchedServers.Clear();
        foreach (var server in sortList)
        {
            var vm = new ServerEntryViewModel(_windowVm, server, _serverListCache, _windowVm.Cfg);
            _searchedServers.Add(vm);
        }
    }

    private bool DoesSearchMatch(ServerStatusData data)
    {
        if (string.IsNullOrWhiteSpace(SearchString))
            return true;

        return data.Name != null &&
               data.Name.Contains(SearchString, StringComparison.CurrentCultureIgnoreCase);
    }

    private sealed class ServerSortComparer : NotNullComparer<ServerStatusData>
    {
        public static readonly ServerSortComparer Instance = new();

        public override int Compare(ServerStatusData x, ServerStatusData y)
        {
            // Sort by player count descending.
            var res = x.PlayerCount.CompareTo(y.PlayerCount);
            if (res != 0)
                return -res;

            // Sort by name.
            res = string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
            if (res != 0)
                return res;

            // Sort by address.
            return string.Compare(x.Address, y.Address, StringComparison.Ordinal);
        }
    }
}
