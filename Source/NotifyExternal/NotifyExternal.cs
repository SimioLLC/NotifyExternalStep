using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;

using SimioAPI;
using SimioAPI.Extensions;
using System.Globalization;
using System.CodeDom;
using System.Runtime.InteropServices;
using System.Data.SqlClient;
using System.Web;
using System.Runtime.Remoting.Messaging;

namespace NotifyExternal
{

    public enum EnumArgLogic
    {
        None = 0,
        Delimited = 1,
        Python = 2
    }

    public enum EnumNotificationType
    {
        Information,
        Warning,
        Error,
    }

    /// <summary>
    /// Format of the response
    /// </summary>
    public enum EnumSendProvider
    {
        File = 0,
        Email = 1,
        Mqtt = 2
    }

    /// <summary>
    /// A Step to notify an external process with data (message) 
    /// </summary>
    class NotifyExternalDefinition : IStepDefinition
    {
        #region IStepDefinition Members

        /// <summary>
        /// Property returning the full name for this type of step. The name should contain no spaces.
        /// </summary>
        public string Name
        {
            get { return "NotifyExternal"; }
        }

        /// <summary>
        /// Property returning a short description of what the step does.
        /// </summary>
        public string Description
        {
            get { return "Run an executable program with optional arguments"; }
        }

        /// <summary>
        /// Property returning an icon to display for the step in the UI.
        /// </summary>
        public System.Drawing.Image Icon
        {
            get { return null; }
        }

        /// <summary>
        /// Property returning a unique static GUID for the step.
        /// </summary>
        public Guid UniqueID
        {
            get { return MY_ID; }
        }
        static readonly Guid MY_ID = new Guid("{A93E9C4F-DA99-4AB5-A35E-DF502C94F7EF}"); // 15Jan2023/NotifyExternal/DHouck
        /// <summary>
        /// Property returning the number of exits out of the step. Can return either 1 or 2.
        /// </summary>
        public int NumberOfExits
        {
            get { return 2; } // Alternate exit if errors
        }

        /// <summary>
        /// Method called that defines the property schema for the step.
        /// </summary>
        public void DefineSchema(IPropertyDefinitions schema)
        {
            try
            {

                // The basic logic
                IEnumPropertyDefinition epd = schema.AddEnumProperty("SendProvider", typeof(EnumSendProvider));
                epd.DisplayName = "SendProvider";
                epd.Description = "The send mechanism for notifying (File, Email, ...)";
                epd.SetDefaultString(schema, EnumSendProvider.File.ToString());
                epd.Required = true;

                epd = schema.AddEnumProperty("NotificationType", typeof(EnumNotificationType));
                epd.DisplayName = "NotificationType";
                epd.Description = "How the notification will be handled. This is determined by the external provider";
                epd.SetDefaultString(schema, "Trace");
                epd.Required = true;

                // The address used for sending the message. Depends upon provider, so:
                // for File: resolves to a file path
                // for MQTT: resolves to a server and topic
                
                IPropertyDefinition pd;
                pd = schema.AddStringProperty("MessageAddress", "");
                pd.DisplayName = "Message Address";
                pd.Description = "How to locate the destination (depends on Notification Type) Begins with FILE: or MQTT: or ??";
                pd.Required = true;

                pd = schema.AddStringProperty("MessageHeading", "");
                pd.DisplayName = "Message Heading";
                pd.Description = "The header for the message";
                pd.Required = true;

                pd = schema.AddStringProperty("MessageContent", "");
                pd.DisplayName = "Message Content";
                pd.Description = "The content of the message";
                pd.Required = true;

                // Advanced section
                pd = schema.AddExpressionProperty("NotifyCondition", "");
                pd.CategoryName = "Advanced Options";
                pd.DisplayName = "Notify condition";
                pd.Description = "The condition for a notification. This must be an expresssion that resolve to a boolean.";
                pd.Required = false;

                pd = schema.AddExpressionProperty("ExclusionExpression", "1");
                pd.CategoryName = "Advanced Options";
                pd.DisplayName = "Exclusion Expression";
                pd.Description = "Evaluated at run start to see if Step is excluded as follows: If 1=> proceed to Primary exit. If 2=> proceed to Secondary exit";
                pd.Required = false;


                IBooleanPropertyDefinition bpd = schema.AddBooleanProperty("ExitApplication");
                bpd.DisplayName = "Exit Application";
                bpd.Description = "If True, the application will exit if notify filters are met.";
                bpd.Required = true;
                bpd.SetDefaultString(schema, "False");


            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Error creating NotifyExternals Schema. Err={ex.Message}");
            }

        }

        /// <summary>
        /// Method called to create a new instance of this step type to place in a process.
        /// Returns an instance of the class implementing the IStep interface.
        /// </summary>
        public IStep CreateStep(IPropertyReaders properties)
        {
            return new NotifyExternal(properties);
            
        }

        #endregion
    } // class definition

    //=================================================================================================================
    //=================================================================================================================

    partial class NotifyExternal : IStep
    {
        IPropertyReaders _properties;

        IPropertyReader _prSendProvider;
        IPropertyReader _prNotifyType;

        IPropertyReader _prMessageAddress;
        IPropertyReader _prMessageHeading;
        IPropertyReader _prMessageContent;

        IPropertyReader _prNotifyCondition;
        IPropertyReader _prExclusionExpression;
        IPropertyReader _prShouldExitApplication;

        private IEnumPropertyDefinition _enumPropertyDefinition;

        private SendProvider sendProvider;

        /// <summary>
        /// In the constructor we define the property readers we'll need. 
        /// This is done for efficiency, since we'll need them throughout the execution, so
        /// there is no need to keep creating them.
        /// </summary>
        /// <param name="properties"></param>
        /// <exception cref="ApplicationException"></exception>
        public NotifyExternal(IPropertyReaders properties)
        {
            try
            {

                _properties = properties;
                _prSendProvider = _properties.GetProperty("SendProvider");
                _prNotifyType = _properties.GetProperty("NotificationType");

                _prMessageAddress = _properties.GetProperty("MessageAddress");
                _prMessageHeading = _properties.GetProperty("MessageHeading");
                _prMessageContent = _properties.GetProperty("MessageContent");
                _prShouldExitApplication = _properties.GetProperty("ExitApplication");

                // Advanced Category
                _prNotifyCondition = _properties.GetProperty("NotifyCondition");
                _prExclusionExpression = _properties.GetProperty("ExclusionExpression");
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"NotifyExternals. Perhaps a misnamed Property? Err={ex.Message}");
            }
        }


        /// <summary>
        /// Method called when a process token executes the step.
        /// </summary>
        public ExitType Execute(IStepExecutionContext context)
        {
            EnumSendProvider enumSendProvider = EnumSendProvider.File;
            EnumNotificationType enumNotifyType = EnumNotificationType.Information;
            string messageAddress = "";
            string messageHeading = "";
            string messageContent = "";
            bool exitApplication = false;

            try
            {
                exitApplication = _prShouldExitApplication.GetDoubleValue(context) != 0;
                TraceIt(context, $"ShouldExitApplication. Bool={exitApplication}");

                // Handle notification type
                string notifyTypeString = _prNotifyType.GetStringValue(context);
                if (Enum.TryParse(notifyTypeString, out enumNotifyType))
                    TraceIt(context, $"NotifyType={notifyTypeString} Enum={enumNotifyType}");
                else
                    enumNotifyType = EnumNotificationType.Information;


                messageAddress = _prMessageAddress.GetStringValue(context);
                messageHeading = _prMessageHeading.GetStringValue(context);
                messageContent = _prMessageContent.GetStringValue(context);

                // Handle send provider. For efficiency this is only done once
                if ( sendProvider == null ) 
                {
                    string sendProviderString = _prSendProvider.GetStringValue(context);
                    if (Enum.TryParse(sendProviderString, out enumSendProvider))
                        TraceIt(context, $"SendProvider={sendProviderString} Enum={enumSendProvider}");
                    else
                        enumSendProvider = EnumSendProvider.File;

                    switch ( enumSendProvider ) 
                    {
                        case EnumSendProvider.File:
                            {
                                sendProvider = new SendFile(context, messageAddress);
                            }
                            break;

                    }
                }


                sendProvider.Send(enumNotifyType, messageHeading, messageContent);

                if ( exitApplication )
                {
                    System.Environment.Exit(1);
                }

            }
            catch (Exception ex)
            {
                LogIt(context, EnumNotificationType.Error, $"NotifyExternal for Provider={enumSendProvider} Err={ex.Message}");
                return ExitType.AlternateExit;
            }

            return ExitType.FirstExit;
        }

        #region Logging
        /// <summary>
        /// Display a trace line (if Simio has Trace mode on)
        /// </summary>
        /// <param name="context"></param>
        /// <param name="message"></param>
        private void TraceIt(IStepExecutionContext context, string message)
        {
            context.ExecutionInformation.TraceInformation($"{message}");
        }

        /// <summary>
        /// The notifyType will determine what happens
        /// </summary>
        /// <param name="context"></param>
        /// <param name="notifyType"></param>
        /// <param name="message"></param>
        private void LogIt(IStepExecutionContext context, EnumNotificationType notifyType, string message)
        {
            switch (notifyType) 
            {
                case EnumNotificationType.Error:
                    {
                    }
                    break;

                    case EnumNotificationType.Warning: 
                    { 
                    }
                    break;

                case EnumNotificationType.Information:
                    {
                    }
                    break;
            }
        }

        #endregion Logging
    }
}



