#if WINDOWS
using Jalium.UI;
using Jalium.UI.Controls;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Foundation.Collections;
using Windows.UI.Notifications;

namespace Jalium.UI.Notifications
{
    /// <summary>
    /// Toast 通知服务 - 专为 Jalium.UI 框架设计
    /// </summary>
    public  class ToastNotificationService
    {
        private static readonly Dictionary<string, Action<ToastActivatedEventArgs>> _actionHandlers = new();
        private static readonly Dictionary<string, Action<ToastActivatedEventArgs>> _buttonHandlers = new();
        private static bool _isInitialized = false;
        private static readonly object _initLock = new object();

        #region 初始化

        /// <summary>
        /// 初始化 Toast 通知服务（应在 App 启动时调用！！！）
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            lock (_initLock)
            {
                if (_isInitialized) return;

                // 订阅激活事件
                ToastNotificationManagerCompat.OnActivated += OnToastActivated;
                _isInitialized = true;
            }
        }

        /// <summary>
        /// 检查当前进程是否由 Toast 激活启动
        /// </summary>
        public static bool WasActivatedByToast =>
            ToastNotificationManagerCompat.WasCurrentProcessToastActivated();

        #endregion

        #region 核心激活处理

        /// <summary>
        /// Toast 激活事件处理
        /// </summary>
        private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat toastArgs)
        {
            // 解析参数
            var args = ToastArguments.Parse(toastArgs.Argument);
            var userInput = toastArgs.UserInput;

            // 获取 action 类型
            // 安全获取参数值
            string action = args.Contains("action") ? args.Get("action") : "default";
            string buttonId = args.Contains("buttonId") ? args.Get("buttonId") : null;

            // 构建事件参数
            var eventArgs = new ToastActivatedEventArgs
            {
                Action = action,
                ButtonId = buttonId,
                Arguments = args,
                UserInput = userInput,
                OriginalArgs = toastArgs
            };

            // 优先检查特定按钮处理器
            if (!string.IsNullOrEmpty(buttonId) && _buttonHandlers.TryGetValue(buttonId, out var buttonHandler))
            {
                DispatchToUIThread(() => buttonHandler.Invoke(eventArgs));
                return;
            }

            // 然后检查 action 处理器
            if (_actionHandlers.TryGetValue(action, out var actionHandler))
            {
                DispatchToUIThread(() => actionHandler.Invoke(eventArgs));
            }
            else
            {
                OnUnhandledAction?.Invoke(null, eventArgs);
            }
        }

        /// <summary>
        /// 调度到 UI 线程（使用 Jalium.UI 的 Dispatcher）
        /// </summary>
        private static void DispatchToUIThread(Action action)
        {
            // 使用 Jalium.UI 的 Dispatcher.CurrentDispatcher 获取当前线程的调度器
            // 如果不在 UI 线程，使用 MainDispatcher
            var dispatcher = Dispatcher.CurrentDispatcher ?? Dispatcher.MainDispatcher;

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                // 如果没有 Dispatcher 或已在目标线程，直接执行
                action();
            }
        }

        /// <summary>
        /// 未处理 action 的全局事件
        /// </summary>
        public static event EventHandler<ToastActivatedEventArgs> OnUnhandledAction;

        #endregion

        #region 处理器注册

        /// <summary>
        /// 注册 Action 处理器
        /// </summary>
        public static void RegisterActionHandler(string action, Action<ToastActivatedEventArgs> handler)
        {
            _actionHandlers[action] = handler;
        }

        /// <summary>
        /// 注册特定按钮处理器（通过 buttonId 区分）
        /// </summary>
        public static void RegisterButtonHandler(string buttonId, Action<ToastActivatedEventArgs> handler)
        {
            _buttonHandlers[buttonId] = handler;
        }

        /// <summary>
        /// 注销处理器
        /// </summary>
        public static void UnregisterActionHandler(string action)
        {
            _actionHandlers.Remove(action);
        }

        /// <summary>
        /// 注销按钮处理器
        /// </summary>
        /// <param name="buttonId"></param>
        public static void UnregisterButtonHandler(string buttonId)
        {
            _buttonHandlers.Remove(buttonId);
        }

        /// <summary>
        /// 清除通知
        /// </summary>
        public static void ClearAllHandlers()
        {
            _actionHandlers.Clear();
            _buttonHandlers.Clear();
        }

        #endregion

        #region 发送通知方法

        /// <summary>
        /// 发送简单通知
        /// </summary>
        public static void Show(string title, string message, string action = "default")
        {
            new ToastContentBuilder()
                .AddArgument("action", action)
                .AddText(title)
                .AddText(message)
                .Show();
        }

        /// <summary>
        /// 发送带自定义参数的通知
        /// </summary>
        public static void ShowWithArguments(string title, string message,
            Dictionary<string, string> arguments)
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            foreach (var arg in arguments)
            {
                builder.AddArgument(arg.Key, arg.Value);
            }

            builder.Show();
        }

        /// <summary>
        /// 发送带按钮的通知（支持独立按钮事件）
        /// </summary>
        public static void ShowWithButtons(string title, string message,
            params ToastButtonInfo[] buttons)
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            foreach (var btn in buttons)
            {
                var toastBtn = new ToastButton()
                    .SetContent(btn.Text)
                    .AddArgument("action", btn.Action);

                // 如果有 buttonId，添加到参数中用于精确路由
                if (!string.IsNullOrEmpty(btn.ButtonId))
                {
                    toastBtn.AddArgument("buttonId", btn.ButtonId);
                }

                // 添加额外自定义参数
                foreach (var arg in btn.Arguments ?? new Dictionary<string, string>())
                {
                    toastBtn.AddArgument(arg.Key, arg.Value);
                }

                if (btn.IsBackground)
                {
                    toastBtn.SetBackgroundActivation();
                }

                builder.AddButton(toastBtn);
            }

            builder.Show();
        }

        /// <summary>
        /// 发送带输入框的通知（快速回复）
        /// </summary>
        public static void ShowWithInput(string title, string message,
            string inputId, string placeholder, string buttonText, string buttonAction,
            string buttonId = null)
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddInputTextBox(inputId, placeholder);

            var btn = new ToastButton()
                .SetContent(buttonText)
                .AddArgument("action", buttonAction)
                .SetBackgroundActivation();

            if (!string.IsNullOrEmpty(buttonId))
            {
                btn.AddArgument("buttonId", buttonId);
            }

            builder.AddButton(btn).Show();
        }

        /// <summary>
        /// 发送富媒体通知（带图片和多个按钮）
        /// </summary>
        public static void ShowRichNotification(string title, string message,
            Uri heroImage, Uri appLogo, ToastGenericAppLogoCrop crop,
            params ToastButtonInfo[] buttons)
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            if (heroImage != null)
                builder.AddHeroImage(heroImage);

            if (appLogo != null)
                builder.AddAppLogoOverride(appLogo, crop);

            foreach (var btn in buttons)
            {
                var toastBtn = new ToastButton()
                    .SetContent(btn.Text)
                    .AddArgument("action", btn.Action);

                if (!string.IsNullOrEmpty(btn.ButtonId))
                    toastBtn.AddArgument("buttonId", btn.ButtonId);

                if (btn.IsBackground)
                    toastBtn.SetBackgroundActivation();

                builder.AddButton(toastBtn);
            }

            builder.Show();
        }

        /// <summary>
        /// 发送进度通知（可更新）
        /// </summary>
        public static NotificationData ShowProgress(string title, string status,
            double? value, string valueString, string tag = "progress", string group = "downloads")
        {
            var data = new NotificationData { SequenceNumber = 0 };

            new ToastContentBuilder()
                .AddText(title)
                .AddProgressBar(status, value, false, null, valueString)
                .Show(toast =>
                {
                    toast.Data = data;
                    toast.Tag = tag;
                    toast.Group = group;
                });

            return data;
        }

        /// <summary>
        /// 更新进度通知
        /// </summary>
        public static void UpdateProgress(NotificationData data, double value,
            string valueString, string status = null, string tag = "progress", string group = "downloads")
        {
            data.Values["progressValue"] = value.ToString();
            data.Values["progressValueString"] = valueString;
            if (status != null)
                data.Values["progressStatus"] = status;

            data.SequenceNumber++;
            ToastNotificationManagerCompat.CreateToastNotifier().Update(data, tag, group);
        }

        /// <summary>
        /// 发送提醒式通知（带休眠/关闭按钮）
        /// </summary>
        public static void ShowReminder(string title, string message,
            string snoozeAction = "snooze", string dismissAction = "dismiss")
        {
            new ToastContentBuilder()
                .SetToastScenario(ToastScenario.Reminder)
                .AddText(title)
                .AddText(message)
                .AddButton(new ToastButton()
                    .SetContent("休眠")
                    .AddArgument("action", snoozeAction)
                    .SetBackgroundActivation())
                .AddButton(new ToastButton()
                    .SetContent("关闭")
                    .AddArgument("action", dismissAction)
                    .SetBackgroundActivation())
                .Show();
        }

        /// <summary>
        /// 发送定时通知
        /// </summary>
        public static void Schedule(string title, string message, DateTimeOffset deliveryTime,
            string tag = null, string group = null, Dictionary<string, string> arguments = null)
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            if (arguments != null)
            {
                foreach (var arg in arguments)
                {
                    builder.AddArgument(arg.Key, arg.Value);
                }
            }

            builder.Schedule(deliveryTime, toast =>
            {
                if (!string.IsNullOrEmpty(tag)) toast.Tag = tag;
                if (!string.IsNullOrEmpty(group)) toast.Group = group;
            });
        }

        #endregion

        #region 通知管理

        public static void ClearAll() => ToastNotificationManagerCompat.History.Clear();

        public static void ClearByTag(string tag, string group = null)
        {
            if (!string.IsNullOrEmpty(group))
                ToastNotificationManagerCompat.History.Remove(tag, group);
            else
                ToastNotificationManagerCompat.History.Remove(tag);
        }

        public static void ClearByGroup(string group)
        {
            ToastNotificationManagerCompat.History.RemoveGroup(group);
        }

        #endregion
    }

    /// <summary>
    /// Toast 激活事件参数
    /// </summary>
    public class ToastActivatedEventArgs : EventArgs
    {
        /// <summary>Action 类型</summary>
        public string Action { get; set; }

        /// <summary>按钮标识（如果有）</summary>
        public string ButtonId { get; set; }

        /// <summary>所有参数</summary>
        public ToastArguments Arguments { get; set; }

        /// <summary>用户输入（文本框内容等）</summary>
        public ValueSet UserInput { get; set; }

        /// <summary>原始事件参数</summary>
        public ToastNotificationActivatedEventArgsCompat OriginalArgs { get; set; }

        /// <summary>获取参数值</summary>
        public string GetArgument(string key) => Arguments?.Get(key);

        /// <summary>获取用户输入值 - 修复：显式指定类型参数</summary>
        public string GetUserInput(string key)
        {
            if (UserInput == null) return null;

            // 显式指定类型参数 object, object
            if (UserInput.TryGetValue(key, out object value))
            {
                return value?.ToString();
            }
            return null;
        }
    }

    /// <summary>
    /// 按钮信息配置
    /// </summary>
    public class ToastButtonInfo
    {
        /// <summary>按钮显示文本</summary>
        public string Text { get; set; }

        /// <summary>Action 类型</summary>
        public string Action { get; set; }

        /// <summary>按钮唯一标识（用于精确路由事件）</summary>
        public string ButtonId { get; set; }

        /// <summary>是否为后台激活</summary>
        public bool IsBackground { get; set; } = true;

        /// <summary>额外参数</summary>
        public Dictionary<string, string> Arguments { get; set; }

        public ToastButtonInfo(string text, string action)
        {
            Text = text;
            Action = action;
        }

        public ToastButtonInfo(string text, string action, string buttonId) : this(text, action)
        {
            ButtonId = buttonId;
        }
    }
}
#endif