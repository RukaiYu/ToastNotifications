﻿using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using MS.WindowsAPICodePack.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ToastNotifications.Share;
using ToastNotifications.Share.ActionButtons;
#if WIN8
using ToastNotifications.Win8.ShellHelpers;
#endif
#if WIN10
using ToastNotifications.Win10.ShellHelpers;
#endif

using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

#if WIN8
namespace ToastNotifications.Win8
#endif
#if WIN10
namespace ToastNotifications.Win10
#endif

{
    /// <summary>
    /// 
    /// </summary>
    public class ToastNotificationRepresenter : IToastNotificationRepresenter
    {
        private readonly string _appId;

        private Dictionary<string, ToastNotification> _notifications = new Dictionary<string, ToastNotification>();

        public ToastNotificationRepresenter(string appId, string appName, string defaultIconFilePath)
        {
            Setup(appId, appName, defaultIconFilePath);
            _appId = appId;
        }

        #region Init

        /// <summary>
        /// 
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="shortcutPath"></param>
        /// <param name="pszIconPath"></param>
        private void InstallShortcut(string appId, string shortcutPath, string pszIconPath)
        {
            // Find the path to the current executable
            String exePath = Process.GetCurrentProcess().MainModule.FileName;
            IShellLinkW newShortcut = (IShellLinkW)new CShellLink();

            // Create a shortcut to the exe
            ShellHelpers.ErrorHelper.VerifySucceeded(newShortcut.SetIconLocation(pszIconPath, 0));
            ShellHelpers.ErrorHelper.VerifySucceeded(newShortcut.SetPath(exePath));
            ShellHelpers.ErrorHelper.VerifySucceeded(newShortcut.SetArguments(""));

            // Open the shortcut property store, set the AppUserModelId property
            IPropertyStore newShortcutProperties = (IPropertyStore)newShortcut;

            using (PropVariant appIdPv = new PropVariant(appId))
            {
                ShellHelpers.ErrorHelper.VerifySucceeded(newShortcutProperties.SetValue(SystemProperties.System.AppUserModel.ID, appIdPv));
                ShellHelpers.ErrorHelper.VerifySucceeded(newShortcutProperties.Commit());
            }

            // Commit the shortcut to disk
            IPersistFile newShortcutSave = (IPersistFile)newShortcut;

            ShellHelpers.ErrorHelper.VerifySucceeded(newShortcutSave.Save(shortcutPath, true));
        }

        // In order to display toasts, a desktop application must have a shortcut on the Start menu.
        // Also, an AppUserModelID must be set on that shortcut.
        // The shortcut should be created as part of the installer. The following code shows how to create
        // a shortcut and assign an AppUserModelID using Windows APIs. You must download and include the 
        // Windows API Code Pack for Microsoft .NET Framework for this code to function
        //
        // Included in this project is a wxs file that be used with the WiX toolkit
        // to make an installer that creates the necessary shortcut. One or the other should be used.
        public bool Setup(string appId, string appName, string defaultIconFilePath)
        {
            string shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + $"\\Microsoft\\Windows\\Start Menu\\Programs\\{appName}.lnk";
            if (!File.Exists(shortcutPath))
            {
                InstallShortcut(appId, shortcutPath, defaultIconFilePath);
                return true;
            }
            return false;
        }

        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="notification"></param>
        public void ShowTwoLines(TwoLinesToastNotificationInfo notification)
        {
            // Get a toast XML template
            XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastImageAndText02);

            // Fill in the text elements
            XmlNodeList stringElements = toastXml.GetElementsByTagName("text");

            stringElements[0].AppendChild(toastXml.CreateTextNode(notification.FirstLineText));
            stringElements[1].AppendChild(toastXml.CreateTextNode(notification.SecondLineText ?? string.Empty));

            // Specify the absolute path to an image
            String imagePath = "file:///" + Path.GetFullPath(notification.IconImagePath);
            XmlNodeList imageElements = toastXml.GetElementsByTagName("image");
            imageElements[0].Attributes.GetNamedItem("src").NodeValue = imagePath;

            // Create the toast and attach event listeners
            Display(notification, toastXml);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="notification"></param>
        public void ShowIncomingCallNotification(IncomingCallNotificationInfo notification)
        {
            var toastContent = new XmlDocument();

            toastContent.LoadXml($@"<toast launch=""action={notification.DefaultAction ?? string.Empty}"" scenario=""incomingCall"">

  <visual>
    <binding template=""ToastGeneric"">
      <text>{notification.FirstLineText}</text>
      <text>{notification.SecondLineText}</text>
    {
    (!string.IsNullOrWhiteSpace(notification.AvatarUrl) ? @"<image hint-crop=""circle"" src=""" + notification.AvatarUrl + "\"/>" : "")
}
    </binding>
  </visual>

  <actions>

    {appendActionButtons(notification)}

  </actions>

</toast>");

            // Create the toast notification
            Display(notification, toastContent);
        }

        private void Display(ToastNotificationInfo notification, XmlDocument toastContent)
        {
            var toastNotif = new ToastNotification(toastContent);

#if WIN10
            toastNotif.Tag = notification.Tag;
#endif

            toastNotif.Activated += (o, e) => notification.Activated?.Invoke(o, new Share.ToastActivatedEventArgs
            {
                Arguements = (e as Windows.UI.Notifications.ToastActivatedEventArgs)?.Arguments,
                Tag = notification.Tag,
            });

            toastNotif.Dismissed += (o, e) => notification.Dismissed?.Invoke(o, new Share.ToastDismissedEventArgs
            {
                Reason = ConvertReason(e.Reason),
            });

            toastNotif.Failed += (o, e) => notification.Failed?.Invoke(this, new Share.ToastFailedEventArgs() { ErrorCode = e.ErrorCode });

            if (!string.IsNullOrWhiteSpace(notification.Tag))
            {
                _notifications[notification.Tag] = toastNotif;
            }

            // And send the notification
            ToastNotificationManager.CreateToastNotifier(notification.AppId).Show(toastNotif);
        }

        private Share.ToastDismissalReason ConvertReason(Windows.UI.Notifications.ToastDismissalReason reason)
        {
            switch (reason)
            {
                case Windows.UI.Notifications.ToastDismissalReason.UserCanceled:
                    return Share.ToastDismissalReason.UserCanceled;
                case Windows.UI.Notifications.ToastDismissalReason.ApplicationHidden:
                    return Share.ToastDismissalReason.ApplicationHidden;
                case Windows.UI.Notifications.ToastDismissalReason.TimedOut:
                    return Share.ToastDismissalReason.TimedOut;
                default:
                    return Share.ToastDismissalReason.Other;
            }
        }

        private string appendActionButtons(IncomingCallNotificationInfo notification)
        {
            if (notification.ActionButtons != null)
            {
                var buttonsBuilder = new StringBuilder();
                foreach (var button in notification.ActionButtons)
                {
                    var buttonXml = $@"
<action
   content=""{button.Content}""
   imageUri=""{button.IconUrl}""
   activationType=""{button.ActivationType.ToStringOne()}""
   arguments=""{button.Arguements}""/>";
                    buttonsBuilder.AppendLine(buttonXml);
                }

                return buttonsBuilder
                    .AppendLine()
                    .ToString();
            }

            return string.Empty;
        }

        public void Dismiss(string tag)
        {
            if (tag != null && _notifications.ContainsKey(tag))
            {
                ToastNotificationManager.CreateToastNotifier(_appId).Hide(_notifications[tag]);

                _notifications.Remove(tag);
            }
        }
    }
}
