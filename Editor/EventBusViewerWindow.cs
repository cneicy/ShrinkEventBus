#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShrinkEventBus.Editor
{
    public class EventBusViewerWindow : EditorWindow
    {
        private enum LiveLogFilterMode
        {
            All,
            HasListeners,
            NoListeners
        }

        private sealed class HandlerDisplayData
        {
            public object TargetInstance { get; set; }
            public MonoBehaviour PingTarget { get; set; }
            public string TargetName { get; set; }
            public string MethodName { get; set; }
            public object Priority { get; set; }
            public string ExecType { get; set; }
            public bool IsStaticHandler { get; set; }
        }

        private sealed class EventLogRecord
        {
            public DateTime Timestamp { get; set; }
            public string TimestampText { get; set; }
            public string EventName { get; set; }
            public string SenderInfo { get; set; }
            public bool HasListeners { get; set; }
            public bool IsCancelable { get; set; }
            public bool IsCanceled { get; set; }
            public string Result { get; set; }
            public string Phase { get; set; }
            public List<string> Parameters { get; set; }
            public List<HandlerDisplayData> Handlers { get; set; }
            public string MatchText { get; set; }
        }

        private sealed class EventLogGroup
        {
            public string EventName { get; set; }
            public List<EventLogRecord> Records { get; } = new();
            public EventLogRecord LatestRecord => Records[0];
        }

        private ScrollView _subscribersScrollView;
        private ScrollView _liveTrackerScrollView;
        private VisualElement _subscribersTab;
        private VisualElement _liveTrackerTab;
        private Label _instanceCountLabel;
        private Label _liveLogStatsLabel;
        private string _searchString = "";
        private string _liveLogSearchString = "";
        private LiveLogFilterMode _liveLogFilterMode = LiveLogFilterMode.All;
        private bool _collapseSameEventType = true;
        private const int MaxLogCount = 100;
        private readonly List<EventLogRecord> _logRecords = new();

        [MenuItem("ShrinkSDK/事件总线/事件查看器")]
        public static void ShowWindow()
        {
            var window = GetWindow<EventBusViewerWindow>("EventBus 控制台");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        private void OnEnable() => EventBus.OnEventTriggeredForEditor += RecordEventLog;
        private void OnDisable() => EventBus.OnEventTriggeredForEditor -= RecordEventLog;

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.Add(CreateHeader());
            root.Add(CreateTabBar());

            var contentContainer = new VisualElement { style = { flexGrow = 1 } };
            _subscribersTab = CreateSubscribersTab();
            _liveTrackerTab = CreateLiveTrackerTab();
            _subscribersTab.style.display = DisplayStyle.Flex;
            _liveTrackerTab.style.display = DisplayStyle.None;

            contentContainer.Add(_subscribersTab);
            contentContainer.Add(_liveTrackerTab);
            root.Add(contentContainer);

            RefreshSubscribersView();

            root.schedule.Execute(() =>
            {
                _instanceCountLabel.text = Application.isPlaying
                    ? $"活跃实例数: {EventBus.GetRegisteredInstanceCount()}"
                    : "游戏未运行";
            }).Every(500);
        }

        private VisualElement CreateHeader()
        {
            var header = new VisualElement
            {
                style =
                {
                    paddingTop = 10, paddingBottom = 10, paddingLeft = 10, paddingRight = 10,
                    backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 1f)), borderBottomWidth = 1,
                    borderBottomColor = new StyleColor(Color.black)
                }
            };
            header.Add(new Label("Shrink Event Bus 控制台")
                { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold } });
            var debugToggle = new Toggle("启用 Debug 调试记录 (追踪实时事件)")
                { value = EventBus.EnableDebugRecord, style = { marginTop = 5 } };
            debugToggle.RegisterValueChangedCallback(evt => EventBus.EnableDebugRecord = evt.newValue);
            header.Add(debugToggle);
            var infoRow = new VisualElement
                { style = { flexDirection = FlexDirection.Row, marginTop = 5, justifyContent = Justify.SpaceBetween } };
            _instanceCountLabel = new Label("活跃实例数: 0") { style = { color = new StyleColor(Color.cyan) } };
            infoRow.Add(_instanceCountLabel);
            header.Add(infoRow);
            return header;
        }

        private VisualElement CreateTabBar()
        {
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(() => SwitchTab(0))
                { text = "订阅者全览", style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleCenter } });
            toolbar.Add(new ToolbarButton(() => SwitchTab(1))
                { text = "实时触发日志", style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleCenter } });
            return toolbar;
        }

        private VisualElement CreateSubscribersTab()
        {
            var container = new VisualElement { style = { flexGrow = 1 } };
            var toolbar = new Toolbar();
            var searchField = new ToolbarSearchField { style = { flexGrow = 1 } };
            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchString = evt.newValue;
                RefreshSubscribersView();
            });
            toolbar.Add(searchField);
            toolbar.Add(new ToolbarButton(RefreshSubscribersView) { text = "刷新视图" });
            container.Add(toolbar);
            _subscribersScrollView = new ScrollView
                { style = { flexGrow = 1, paddingLeft = 5, paddingRight = 5, paddingTop = 5 } };
            container.Add(_subscribersScrollView);
            return container;
        }

        private VisualElement CreateLiveTrackerTab()
        {
            var container = new VisualElement { style = { flexGrow = 1 } };
            var toolbar = new Toolbar();
            var searchField = new ToolbarSearchField { style = { flexGrow = 1 } };
            searchField.RegisterValueChangedCallback(evt =>
            {
                _liveLogSearchString = evt.newValue ?? string.Empty;
                RefreshLiveTrackerView();
            });
            toolbar.Add(searchField);

            var filterMenu = new ToolbarMenu { text = "全部事件" };
            filterMenu.menu.AppendAction("全部事件", _ =>
            {
                _liveLogFilterMode = LiveLogFilterMode.All;
                filterMenu.text = "全部事件";
                RefreshLiveTrackerView();
            });
            filterMenu.menu.AppendAction("仅有监听者", _ =>
            {
                _liveLogFilterMode = LiveLogFilterMode.HasListeners;
                filterMenu.text = "仅有监听者";
                RefreshLiveTrackerView();
            });
            filterMenu.menu.AppendAction("仅无监听者", _ =>
            {
                _liveLogFilterMode = LiveLogFilterMode.NoListeners;
                filterMenu.text = "仅无监听者";
                RefreshLiveTrackerView();
            });
            toolbar.Add(filterMenu);

            var collapseToggle = new ToolbarToggle
            {
                text = "折叠同类事件",
                value = _collapseSameEventType
            };
            collapseToggle.RegisterValueChangedCallback(evt =>
            {
                _collapseSameEventType = evt.newValue;
                RefreshLiveTrackerView();
            });
            toolbar.Add(collapseToggle);
            toolbar.Add(new ToolbarButton(ClearLogs) { text = "清空日志" });
            container.Add(toolbar);

            _liveLogStatsLabel = new Label
            {
                style =
                {
                    marginLeft = 8,
                    marginRight = 8,
                    marginTop = 4,
                    marginBottom = 4,
                    color = new StyleColor(Color.gray)
                }
            };
            container.Add(_liveLogStatsLabel);

            _liveTrackerScrollView = new ScrollView { style = { flexGrow = 1, paddingLeft = 5, paddingRight = 5 } };
            container.Add(_liveTrackerScrollView);
            RefreshLiveTrackerView();
            return container;
        }

        private void SwitchTab(int index)
        {
            _subscribersTab.style.display = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _liveTrackerTab.style.display = index == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (index == 0) RefreshSubscribersView();
        }

        private void RefreshSubscribersView()
        {
            _subscribersScrollView.Clear();
            if (!Application.isPlaying)
            {
                _subscribersScrollView.Add(new HelpBox("请先运行游戏，系统初始化后即可查看内存中的事件树。", HelpBoxMessageType.Info));
                return;
            }

            var field = typeof(EventBus).GetField("EventHandlers", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) return;
            var dict = field.GetValue(null) as IDictionary;
            if (dict == null) return;

            foreach (DictionaryEntry entry in dict)
            {
                var eventType = entry.Key as Type;
                if (!string.IsNullOrEmpty(_searchString) &&
                    !eventType.Name.ToLower().Contains(_searchString.ToLower())) continue;

                var listenerListObj = entry.Value;
                var handlers =
                    listenerListObj.GetType().GetMethod("GetHandlers")?.Invoke(listenerListObj, null) as Array;

                var foldout = new Foldout
                {
                    text = $"{eventType.Name}  (挂载数: {handlers.Length})",
                    style =
                    {
                        backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.5f)),
                        marginTop = 2,
                        paddingBottom = 2,
                        borderBottomWidth = 1,
                        borderBottomColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f))
                    }
                };

                foreach (var h in handlers) foldout.Add(CreateHandlerRow(h));
                _subscribersScrollView.Add(foldout);
            }
        }

        private void RecordEventLog(EventBase evt, string eventName, string senderInfo)
        {
            if (!EventBus.EnableDebugRecord) return;
            _logRecords.Insert(0, CreateEventLogRecord(evt, eventName, senderInfo));
            if (_logRecords.Count > MaxLogCount)
                _logRecords.RemoveAt(_logRecords.Count - 1);
            RefreshLiveTrackerView();
        }

        private EventLogRecord CreateEventLogRecord(EventBase evt, string eventName, string senderInfo)
        {
            var timestamp = DateTime.Now;
            var list = evt.GetListenerList();
            var handlers = list?.GetHandlers() ?? Array.Empty<EventHandlerInfo>();
            var hasListeners = handlers.Length > 0;
            var paramsList = DumpEventParams(evt);
            var handlerRows = new List<HandlerDisplayData>(handlers.Length);
            foreach (var handler in handlers)
                handlerRows.Add(ExtractHandlerDisplayData(handler));

            var isCancelable = (GetMemberValue(evt, "IsCancelable") as bool?) ?? false;
            var isCanceled = (GetMemberValue(evt, "IsCanceled") as bool?) ?? false;
            var resultStr = GetMemberValue(evt, "Result")?.ToString() ?? "DEFAULT";
            var phaseStr = GetMemberValue(evt, "Phase")?.ToString() ?? "NORMAL";

            return new EventLogRecord
            {
                Timestamp = timestamp,
                TimestampText = timestamp.ToString("HH:mm:ss.fff"),
                EventName = eventName,
                SenderInfo = senderInfo,
                HasListeners = hasListeners,
                IsCancelable = isCancelable,
                IsCanceled = isCanceled,
                Result = resultStr,
                Phase = phaseStr,
                Parameters = paramsList,
                Handlers = handlerRows,
                MatchText = BuildLogMatchText(eventName, senderInfo, paramsList)
            };
        }

        private string BuildLogMatchText(string eventName, string senderInfo, List<string> paramsList)
        {
            if (paramsList.Count == 0)
                return $"{eventName}\n{senderInfo}";
            return $"{eventName}\n{senderInfo}\n{string.Join("\n", paramsList)}";
        }

        private void RefreshLiveTrackerView()
        {
            if (_liveTrackerScrollView == null) return;

            _liveTrackerScrollView.Clear();

            var filteredRecords = new List<EventLogRecord>();
            foreach (var record in _logRecords)
            {
                if (MatchesLiveLogFilter(record))
                    filteredRecords.Add(record);
            }

            if (_liveLogStatsLabel != null)
            {
                _liveLogStatsLabel.text = _collapseSameEventType
                    ? $"显示 {filteredRecords.Count} / {_logRecords.Count} 条，按事件类型折叠"
                    : $"显示 {filteredRecords.Count} / {_logRecords.Count} 条";
            }

            if (filteredRecords.Count == 0)
            {
                _liveTrackerScrollView.Add(new HelpBox("当前没有符合条件的实时事件日志。", HelpBoxMessageType.Info));
                return;
            }

            if (_collapseSameEventType)
            {
                foreach (var group in BuildCollapsedGroups(filteredRecords))
                    _liveTrackerScrollView.Add(CreateCollapsedGroupElement(group));
                return;
            }

            foreach (var record in filteredRecords)
                _liveTrackerScrollView.Add(CreateLogEntryElement(record));
        }

        private bool MatchesLiveLogFilter(EventLogRecord record)
        {
            switch (_liveLogFilterMode)
            {
                case LiveLogFilterMode.HasListeners:
                    if (!record.HasListeners) return false;
                    break;
                case LiveLogFilterMode.NoListeners:
                    if (record.HasListeners) return false;
                    break;
            }

            if (string.IsNullOrWhiteSpace(_liveLogSearchString))
                return true;

            return record.MatchText.IndexOf(_liveLogSearchString.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private List<EventLogGroup> BuildCollapsedGroups(List<EventLogRecord> records)
        {
            var groups = new List<EventLogGroup>();
            var groupMap = new Dictionary<string, EventLogGroup>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                if (!groupMap.TryGetValue(record.EventName, out var group))
                {
                    group = new EventLogGroup { EventName = record.EventName };
                    groupMap[record.EventName] = group;
                    groups.Add(group);
                }

                group.Records.Add(record);
            }

            return groups;
        }

        private VisualElement CreateCollapsedGroupElement(EventLogGroup group)
        {
            var latest = group.LatestRecord;
            var foldout = new Foldout
            {
                text = $"{group.EventName} × {group.Records.Count}  最近: {latest.TimestampText}",
                value = group.Records.Count == 1,
                style =
                {
                    backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.4f)),
                    marginTop = 2,
                    paddingBottom = 2,
                    borderBottomWidth = 1,
                    borderBottomColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f))
                }
            };

            var summary = new Label(
                $"最近来源: {latest.SenderInfo} | 监听者: {(latest.HasListeners ? latest.Handlers.Count : 0)} | 已取消: {(latest.IsCanceled ? "是" : "否")}")
            {
                style =
                {
                    marginLeft = 18,
                    marginBottom = 4,
                    color = new StyleColor(Color.gray)
                }
            };
            foldout.Add(summary);

            foreach (var record in group.Records)
            {
                var row = CreateLogEntryElement(record);
                row.style.marginLeft = 12;
                foldout.Add(row);
            }

            return foldout;
        }

        private VisualElement CreateLogEntryElement(EventLogRecord record)
        {
            var logEntry = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    paddingBottom = 4,
                    paddingTop = 4,
                    borderBottomWidth = 1,
                    borderBottomColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f))
                }
            };

            var headerRow = new VisualElement
                { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            headerRow.Add(new Label(record.TimestampText)
                { style = { width = 80, color = new StyleColor(Color.gray) } });
            headerRow.Add(new Label(record.EventName)
            {
                style = { color = new StyleColor(new Color(1f, 0.84f, 0f)), unityFontStyleAndWeight = FontStyle.Bold }
            });

            var senderClassName = GetSenderClassName(record.SenderInfo);
            var senderLabel = new Label($" [由 {record.SenderInfo} 触发]")
            {
                style =
                {
                    color = new StyleColor(new Color(0.5f, 0.8f, 1f)),
                    marginLeft = 5,
                    unityFontStyleAndWeight = FontStyle.Italic
                }
            };

            if (senderClassName != "未知来源" && senderClassName != "EventBus" && senderClassName != "Unknown")
            {
                senderLabel.RegisterCallback<MouseEnterEvent>(_ =>
                    senderLabel.style.color = new StyleColor(Color.cyan));
                senderLabel.RegisterCallback<MouseLeaveEvent>(_ =>
                    senderLabel.style.color = new StyleColor(new Color(0.5f, 0.8f, 1f)));
                senderLabel.RegisterCallback<ClickEvent>(_ => OpenScriptByClassName(senderClassName));
            }

            headerRow.Add(senderLabel);
            logEntry.Add(headerRow);

            var statusRow = new VisualElement
                { style = { flexDirection = FlexDirection.Row, marginLeft = 80, marginTop = 2 } };
            statusRow.Add(new Label($"可取消: {(record.IsCancelable ? "是" : "否")}")
                { style = { color = new StyleColor(Color.gray), marginRight = 15 } });

            var canceledLabel = new Label($"已取消: {(record.IsCanceled ? "是" : "否")}")
                { style = { marginRight = 15 } };
            canceledLabel.style.color =
                record.IsCanceled ? new StyleColor(new Color(1f, 0.3f, 0.3f)) : new StyleColor(Color.gray);
            statusRow.Add(canceledLabel);

            statusRow.Add(new Label($"Result: {record.Result}")
                { style = { color = new StyleColor(new Color(0.8f, 0.6f, 0.8f)), marginRight = 15 } });
            statusRow.Add(new Label($"最后阶段: {record.Phase}")
                { style = { color = new StyleColor(new Color(0.6f, 0.8f, 1f)) } });
            logEntry.Add(statusRow);

            var paramsContainer = new VisualElement
                { style = { flexDirection = FlexDirection.Column, marginLeft = 80, marginTop = 2, marginBottom = 2 } };
            if (record.Parameters.Count == 0)
            {
                paramsContainer.Add(new Label("参数: { 无 }")
                    { style = { color = new StyleColor(new Color(0.5f, 0.5f, 0.5f)) } });
            }
            else
            {
                paramsContainer.Add(
                    new Label("携带参数:") { style = { color = new StyleColor(new Color(0.6f, 0.8f, 1f)) } });
                foreach (var pStr in record.Parameters)
                    paramsContainer.Add(new Label($"  • {pStr}")
                        { style = { color = new StyleColor(new Color(0.6f, 0.9f, 0.6f)), marginLeft = 10 } });
            }

            logEntry.Add(paramsContainer);

            if (record.HasListeners)
            {
                foreach (var handler in record.Handlers)
                    logEntry.Add(CreateHandlerRow(handler, 80));
            }
            else
            {
                var noHandlerRow = new VisualElement
                    { style = { flexDirection = FlexDirection.Row, marginLeft = 80, marginTop = 2 } };
                noHandlerRow.Add(new Label("(无监听者响应)") { style = { color = new StyleColor(Color.gray) } });
                logEntry.Add(noHandlerRow);
            }

            return logEntry;
        }

        private string GetSenderClassName(string senderInfo)
        {
            if (string.IsNullOrWhiteSpace(senderInfo))
                return "未知来源";
            return senderInfo.Contains(".") ? senderInfo.Split('.')[0] : senderInfo;
        }

        private VisualElement CreateHandlerRow(object h)
        {
            return CreateHandlerRow(ExtractHandlerDisplayData(h), 40);
        }

        private VisualElement CreateHandlerRow(HandlerDisplayData handlerData, float marginLeft)
        {
            var targetInstance = handlerData.TargetInstance;
            var targetName = handlerData.TargetName;
            var methodName = handlerData.MethodName;
            var priority = handlerData.Priority;
            var execType = handlerData.ExecType;

            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, marginTop = 2, marginBottom = 2, marginLeft = marginLeft,
                    alignItems = Align.Center
                }
            };

            row.Add(new Label($"[{priority}]") { style = { width = 75, color = new StyleColor(Color.gray) } });
            row.Add(new Label($"[{execType}]")
                { style = { width = 50, color = new StyleColor(new Color(0.8f, 0.6f, 0.2f)) } });

            var linkLabel = new Label($"{targetName}.{methodName}()")
                { style = { color = new StyleColor(new Color(0.3f, 0.6f, 1f)) } };
            linkLabel.RegisterCallback<MouseEnterEvent>(e => linkLabel.style.color = new StyleColor(Color.cyan));
            linkLabel.RegisterCallback<MouseLeaveEvent>(e =>
                linkLabel.style.color = new StyleColor(new Color(0.3f, 0.6f, 1f)));
            linkLabel.RegisterCallback<ClickEvent>(e => OpenScriptByClassName(targetName));
            linkLabel.style.flexGrow = 1;
            row.Add(linkLabel);

            if (handlerData.PingTarget)
            {
                var pingBtn = new Button(() =>
                {
                    EditorGUIUtility.PingObject(handlerData.PingTarget.gameObject);
                    Selection.activeGameObject = handlerData.PingTarget.gameObject;
                }) { text = "Ping", style = { width = 50, height = 18 } };
                row.Add(pingBtn);
            }
            else if (handlerData.IsStaticHandler && methodName != "未知方法")
                row.Add(new Label("(静态)")
                {
                    style = { width = 50, color = new StyleColor(Color.gray), unityTextAlign = TextAnchor.MiddleRight }
                });
            else
                row.Add(new Label("(无对象)")
                {
                    style = { width = 50, color = new StyleColor(Color.gray), unityTextAlign = TextAnchor.MiddleRight }
                });

            return row;
        }

        private HandlerDisplayData ExtractHandlerDisplayData(object handlerObj)
        {
            var priority = GetMemberValue(handlerObj, "Priority") ?? "NORMAL";
            var debugInfo = GetMemberValue(handlerObj, "DebugInfo") as string ?? "未知方法";

            var execType = "Sync";
            if (debugInfo.Contains("(UniTask)") || debugInfo.Contains("Async")) execType = "Async";
            else if (debugInfo.Contains("(Task->UniTask)") || debugInfo.Contains("(Task)")) execType = "Task";

            object targetInstance = null;
            var targetName = "未知类";
            var methodName = debugInfo;

            var handlerDelegate = GetMemberValue(handlerObj, "Handler") as Delegate;
            if (handlerDelegate != null)
            {
                targetInstance = handlerDelegate.Target;
                var methodInfo = handlerDelegate.Method;

                if (targetInstance != null && targetInstance.GetType().Name.Contains("Wrapper"))
                {
                    var realDelegate = GetMemberValue(targetInstance, "_originalAction") as Delegate;
                    if (realDelegate != null)
                    {
                        targetInstance = realDelegate.Target;
                        methodInfo = realDelegate.Method;
                    }
                }

                if (methodInfo != null)
                {
                    var rawClassName = methodInfo.DeclaringType?.Name ?? "未知类";
                    if (rawClassName.Contains("<>c")) rawClassName = "匿名闭包类";
                    targetName = rawClassName;
                    methodName = methodInfo.Name;
                }
            }

            return new HandlerDisplayData
            {
                TargetInstance = targetInstance,
                PingTarget = targetInstance as MonoBehaviour,
                TargetName = targetName,
                MethodName = methodName,
                Priority = priority,
                ExecType = execType,
                IsStaticHandler = targetInstance == null
            };
        }

        private List<string> DumpEventParams(EventBase evt)
        {
            var values = new List<string>();
            if (evt == null) return values;

            var currentType = evt.GetType();

            while (currentType != null && currentType != typeof(EventBase) && currentType != typeof(object))
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                            BindingFlags.DeclaredOnly;

                foreach (var f in currentType.GetFields(flags))
                {
                    if (f.Name.Contains("<") || f.Name.Contains(">")) continue;
                    if (f.Name is "CurrentHandler" or "Phase") continue;
                    values.Add($"{f.Name} = {FormatValue(f.GetValue(evt))}");
                }

                foreach (var p in currentType.GetProperties(flags))
                {
                    if (p.Name is "CurrentHandler" or "Phase" or "IsCanceled" or "IsCancelable" or "Result") continue;
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    try
                    {
                        values.Add($"{p.Name} = {FormatValue(p.GetValue(evt))}");
                    }
                    catch
                    {
                        // ignored
                    }
                }

                currentType = currentType.BaseType;
            }

            return values;
        }

        private string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is UnityEngine.Object uObj) return uObj ? uObj.name : "null(已销毁)";
            if (val is string s) return $"\"{s}\"";
            if (val is Array arr) return $"[数组 Length:{arr.Length}]";
            return val.ToString();
        }

        private object GetMemberValue(object obj, string memberName)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null) return prop.GetValue(obj);

            var field = type.GetField(memberName, flags);
            if (field != null) return field.GetValue(obj);

            return null;
        }

        private void ClearLogs()
        {
            _logRecords.Clear();
            RefreshLiveTrackerView();
        }

        private void OpenScriptByClassName(string className)
        {
            if (className == "匿名闭包类") return;
            var guids = AssetDatabase.FindAssets($"{className} t:MonoScript");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith($"{className}.cs"))
                {
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (script)
                    {
                        AssetDatabase.OpenAsset(script);
                        return;
                    }
                }
            }
        }
    }
}
#endif
