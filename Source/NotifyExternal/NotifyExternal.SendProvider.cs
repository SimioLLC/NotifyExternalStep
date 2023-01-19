using NotifyExternal;
using SimioAPI;
using System;
using System.IO;

namespace NotifyExternal
{
    abstract class SendProvider
    {
        protected SendProvider(IStepExecutionContext context, string filepath)
        {

        }

        public abstract void Send(EnumNotificationType notifyType, string messageHeader, string messageBody);



    }

    class SendFile : SendProvider
    {
        string _filepath;
        IStepExecutionContext _context;

        public SendFile(IStepExecutionContext context, string filepath) : base(context, filepath)
        {
            _filepath = filepath;
            _context = context;
        }

        public override void Send(EnumNotificationType notifyType, string messageHeader, string messageBody)
        {
            string info = $"Exp={_context.ExecutionInformation.ExperimentName} Scen={_context.ExecutionInformation.ScenarioName}";
            string line = $"WorldTime={DateTime.Now:HH:mm.ffff} SimTime={_context.Calendar.TimeNow:0.000}:[{info}] {messageHeader}: {messageBody}\n";
            File.AppendAllText(_filepath, line );

        }
    }

    /// <summary>
    /// Stub...
    /// </summary>
    class SendEmail : SendProvider
    {

        public SendEmail(IStepExecutionContext context, string filepath) : base(context, filepath)
        {
            throw new ApplicationException($"Email Provider not implemeneted");
        }

        public override void Send(EnumNotificationType notifyType, string messageHeader, string messageBody)
        {
            throw new ApplicationException($"Email Provider not implemeneted");
        }
    }

    /// <summary>
    /// Stub...
    /// </summary>
    class SendMqtt : SendProvider
    {

        public SendMqtt(IStepExecutionContext context, string filepath) : base(context, filepath)
        {
            throw new ApplicationException($"MQTT Provider not implemeneted");
        }

        public override void Send(EnumNotificationType notifyType, string messageHeader, string messageBody)
        {
            throw new ApplicationException($"MQTT Provider not implemeneted");
        }
    }
}



