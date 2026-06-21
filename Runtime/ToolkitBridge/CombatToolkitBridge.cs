using System;
using System.Collections.Generic;
using NiumaCombat.Enum;
using NiumaCombat.ViewData;
using NiumaUI.Toolkit;
using NiumaUI.Toolkit.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace NiumaCombat.ToolkitBridge
{
    public sealed class CombatToolkitReceiver : MonoBehaviour, ICombatUIReceiver
    {
        [SerializeField, Tooltip("拖核心场景 UIRoot/UIManager 上的 UIToolkitUIManager。")]
        private UIToolkitUIManager uiManager;

        [SerializeField, Tooltip("Combat 面板 ViewId。默认 CombatPanel，需要在 UIToolkitViewRegistrySO 中注册。")]
        private string combatViewId = "CombatPanel";

        [SerializeField, Tooltip("刷新失败时是否自动打开 Combat 面板。")]
        private bool autoOpenView = true;

        [SerializeField, Tooltip("收到 Cleared 更新时是否关闭 Combat 面板。关闭后会立即返回，不会重新打开。")]
        private bool closeOnCleared = true;

        [SerializeField, Tooltip("缺少 UIManager 或 View 时是否输出警告。")]
        private bool logWarnings = true;

        public void ApplyCombatUpdate(CombatUIUpdate update)
        {
            if (update != null && update.UpdateType == CombatUIUpdateType.Cleared && closeOnCleared && uiManager != null)
            {
                uiManager.CloseView(combatViewId);
                return;
            }

            if (!EnsureUIManager())
            {
                return;
            }

            var refreshed = uiManager.RefreshView(combatViewId, update);
            if (!refreshed && autoOpenView)
            {
                refreshed = uiManager.OpenView(combatViewId, update);
            }

            if (!refreshed)
            {
                Warn($"没有刷新到 Combat Toolkit View。ViewId={combatViewId}。请检查 UIToolkitViewRegistrySO 和 CombatToolkitBindingProvider。");
            }
        }

        private bool EnsureUIManager()
        {
            if (uiManager == null)
            {
                uiManager = FindSceneObject<UIToolkitUIManager>();
            }

            if (uiManager != null)
            {
                return true;
            }

            Warn("未绑定 UIToolkitUIManager，Combat Toolkit 面板无法刷新。");
            return false;
        }

        private void Warn(string message)
        {
            if (logWarnings && !string.IsNullOrWhiteSpace(message))
            {
                Debug.LogWarning($"[CombatToolkitReceiver] {message}", this);
            }
        }

        private static T FindSceneObject<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }
    }

    public sealed class CombatToolkitBindingProvider : ToolkitViewBindingProviderBase
    {
        [Header("元素名称")]
        [SerializeField, Tooltip("标题 Label 的 name。默认 TitleText。")]
        private string titleLabelName = "TitleText";

        [SerializeField, Tooltip("状态 Label 的 name。默认 StatusText。")]
        private string statusLabelName = "StatusText";

        [SerializeField, Tooltip("最近战斗结果 ListView 的 name。默认 ListRoot。")]
        private string listViewName = "ListRoot";

        [SerializeField, Tooltip("详情 Label 的 name。用于显示当前选中的 CombatResult。")]
        private string detailLabelName = "DetailText";

        [SerializeField, Tooltip("飘字 / 最新结果 Label 的 name。默认 FloatingText。")]
        private string floatingLabelName = "FloatingText";

        [SerializeField, Tooltip("结果 Label 的 name。用于显示最后一次更新类型。")]
        private string resultLabelName = "ResultText";

        [SerializeField, Tooltip("空状态节点的 name。没有战斗结果时显示。")]
        private string emptyRootName = "EmptyRoot";

        [Header("列表")]
        [SerializeField, Tooltip("最多显示多少条最近结果。")]
        private int maxRows = 40;

        [SerializeField, Tooltip("列表行 USS class。")]
        private string rowClass = "niuma-combat-row";

        [SerializeField, Tooltip("选中行 USS class。")]
        private string selectedRowClass = "is-selected";

        [SerializeField, Tooltip("禁用行 USS class。")]
        private string disabledRowClass = "is-disabled";

        protected override string DefaultProviderId => "CombatPanel";

        public override IToolkitViewBinding CreateBinding()
        {
            return new CombatToolkitBinding(
                titleLabelName,
                statusLabelName,
                listViewName,
                detailLabelName,
                floatingLabelName,
                resultLabelName,
                emptyRootName,
                maxRows,
                rowClass,
                selectedRowClass,
                disabledRowClass);
        }
    }

    public sealed class CombatToolkitViewModel : UIPanelViewModelBase
    {
        public readonly List<ToolkitTextRowData> Rows = new List<ToolkitTextRowData>();
        public CombatPanelViewData Panel { get; private set; }
        public CombatUIUpdateType UpdateType { get; private set; }
        public long Revision { get; private set; }
        public string SelectedRequestId { get; private set; }
        public CombatResultViewData SelectedResult { get; private set; }
        public CombatFloatingTextViewData LatestFloatingText { get; private set; }

        public void Apply(CombatUIUpdate update, int maxRows)
        {
            Panel = update?.Current;
            UpdateType = update?.UpdateType ?? CombatUIUpdateType.Cleared;
            Revision = update?.Revision ?? 0L;
            SetContext(Panel?.ActorId);
            LatestFloatingText = ResolveLatestFloatingText(Panel);
            SelectedRequestId = NormalizeSelection(Panel, update?.Result, SelectedRequestId);
            RebuildRows(maxRows);
            MarkDirty();
        }

        public void Select(string requestId)
        {
            SelectedRequestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim();
            RebuildRows(int.MaxValue);
            MarkDirty();
        }

        protected override void OnClear(UIViewModelClearReason reason)
        {
            Panel = null;
            UpdateType = CombatUIUpdateType.Cleared;
            Revision = 0L;
            SelectedRequestId = null;
            SelectedResult = null;
            LatestFloatingText = null;
            Rows.Clear();
        }

        private void RebuildRows(int maxRows)
        {
            Rows.Clear();
            SelectedResult = null;
            var results = Panel?.RecentResults ?? Array.Empty<CombatResultViewData>();
            var rowsLeft = Math.Max(1, maxRows);
            for (var i = 0; i < results.Length && rowsLeft > 0; i++)
            {
                var result = results[i];
                if (result == null)
                {
                    continue;
                }

                var id = ResolveRowId(result, i);
                var isSelected = string.Equals(SelectedRequestId, id, StringComparison.Ordinal);
                if (isSelected)
                {
                    SelectedResult = result;
                }

                Rows.Add(new ToolkitTextRowData(id, BuildRowText(result), isSelected, true, result));
                rowsLeft--;
            }
        }

        private static string NormalizeSelection(CombatPanelViewData panel, CombatResultViewData latestResult, string previous)
        {
            var results = panel?.RecentResults ?? Array.Empty<CombatResultViewData>();
            if (latestResult != null)
            {
                return ResolveRowId(latestResult, 0);
            }

            if (!string.IsNullOrWhiteSpace(previous))
            {
                for (var i = 0; i < results.Length; i++)
                {
                    if (string.Equals(ResolveRowId(results[i], i), previous, StringComparison.Ordinal))
                    {
                        return previous.Trim();
                    }
                }
            }

            return results.Length > 0 ? ResolveRowId(results[0], 0) : null;
        }

        private static CombatFloatingTextViewData ResolveLatestFloatingText(CombatPanelViewData panel)
        {
            var items = panel?.FloatingTexts ?? Array.Empty<CombatFloatingTextViewData>();
            return items.Length > 0 ? items[0] : null;
        }

        private static string ResolveRowId(CombatResultViewData result, int index)
        {
            if (result == null)
            {
                return $"result:{index}";
            }

            if (!string.IsNullOrWhiteSpace(result.RequestId))
            {
                return result.RequestId.Trim();
            }

            return $"{result.ResultType}:{result.SourceActorId}->{result.TargetActorId}:{result.ResolvedAtUnixMs}:{index}";
        }

        private static string BuildRowText(CombatResultViewData result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            var critical = result.IsCritical ? " | 暴击" : string.Empty;
            var killed = result.IsKilled ? " | 击杀" : string.Empty;
            return $"{result.Message} | {Text(result.SourceActorId, "?")} -> {Text(result.TargetActorId, "?")}{critical}{killed}";
        }

        private static string Text(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
        }
    }

    public sealed class CombatToolkitBinding : ToolkitViewBindingBase<CombatUIUpdate, CombatToolkitViewModel>
    {
        private readonly string _titleName;
        private readonly string _statusName;
        private readonly string _listName;
        private readonly string _detailName;
        private readonly string _floatingName;
        private readonly string _resultName;
        private readonly string _emptyName;
        private readonly int _maxRows;
        private readonly string _rowClass;
        private readonly string _selectedClass;
        private readonly string _disabledClass;
        private readonly ToolkitListBinding<ToolkitTextRowData> _listBinding = new ToolkitListBinding<ToolkitTextRowData>();
        private Label _title;
        private Label _status;
        private Label _detail;
        private Label _floating;
        private Label _result;

        public CombatToolkitBinding(
            string titleName,
            string statusName,
            string listName,
            string detailName,
            string floatingName,
            string resultName,
            string emptyName,
            int maxRows,
            string rowClass,
            string selectedClass,
            string disabledClass)
        {
            _titleName = titleName;
            _statusName = statusName;
            _listName = listName;
            _detailName = detailName;
            _floatingName = floatingName;
            _resultName = resultName;
            _emptyName = emptyName;
            _maxRows = Mathf.Max(1, maxRows);
            _rowClass = string.IsNullOrWhiteSpace(rowClass) ? "niuma-combat-row" : rowClass.Trim();
            _selectedClass = selectedClass;
            _disabledClass = disabledClass;
        }

        protected override void OnInitializeTyped()
        {
            _title = QLabel(_titleName);
            _status = QLabel(_statusName);
            _detail = QLabel(_detailName);
            _floating = QLabel(_floatingName);
            _result = QLabel(_resultName);
            _listBinding.Bind(Root, _listName, new ToolkitTextRowItemBinder(_rowClass, _selectedClass, _disabledClass, HandleRowClicked), _emptyName);
            ApplyVisualState(ViewModel);
        }

        protected override void OnRefreshTyped(CombatUIUpdate viewData, CombatToolkitViewModel viewModel)
        {
            viewModel.Apply(viewData, _maxRows);
            ApplyVisualState(viewModel);
        }

        protected override void OnClearTyped(UIViewModelClearReason reason)
        {
            _listBinding.Clear();
            ApplyVisualState(ViewModel);
        }

        protected override void OnDisposeTyped()
        {
            _listBinding.Dispose();
        }

        private void HandleRowClicked(ToolkitTextRowData row)
        {
            if (row == null)
            {
                return;
            }

            ViewModel.Select(row.Id);
            ApplyVisualState(ViewModel);
        }

        private void ApplyVisualState(CombatToolkitViewModel viewModel)
        {
            var panel = viewModel?.Panel;
            var rows = viewModel?.Rows ?? new List<ToolkitTextRowData>();
            SetText(_title, "战斗结果");
            SetText(_status, panel == null
                ? $"状态：{viewModel?.UpdateType ?? CombatUIUpdateType.Cleared}"
                : $"Actor {Text(panel.ActorId, "未知")} | Revision {panel.Revision} | 结果 {panel.RecentResults?.Length ?? 0} | 飘字 {panel.FloatingTexts?.Length ?? 0}");
            SetText(_floating, viewModel?.LatestFloatingText != null ? viewModel.LatestFloatingText.Text : string.Empty);
            SetText(_detail, viewModel?.SelectedResult != null ? Detail(viewModel.SelectedResult) : panel == null ? "暂无战斗结果。" : "未选择战斗结果。");
            SetText(_result, viewModel == null ? string.Empty : $"最后更新：{viewModel.UpdateType}");
            _listBinding.ReplaceAll(rows);
        }

        private static string Detail(CombatResultViewData result)
        {
            if (result == null)
            {
                return "未选择战斗结果。";
            }

            return $"结果：{result.Message}\n类型：{result.ResultType} / {result.FailureReason}\n来源：{Text(result.SourceActorId, "未知")}\n目标：{Text(result.TargetActorId, "未知")}\n数值：Raw {result.RawValue:0.##} -> Final {result.FinalValue:0.##}\n命中点：{result.HitPoint}\n方向：{result.HitDirection}\nRequestId：{Text(result.RequestId, "无")}".Trim();
        }

        private static string Text(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
        }
    }
}
