namespace PgChatParser
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Windows.Threading;

    public class Parser : IDisposable
    {
        #region Constants
        private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan FolderCheckTimeout = TimeSpan.FromSeconds(30);
        #endregion

        #region Init
        public Parser(Dispatcher dispatcher)
        {
            Dispatcher = dispatcher;
            InitFolders();
        }

        public Dispatcher Dispatcher { get; }
        #endregion

        #region Cleanup
        private void Disconnect()
        {
            if (LogStream != null)
            {
                using (FileStream fs = LogStream) { }
                LogStream = null;
            }
        }
        #endregion

        #region Client Interface
        public void StartLogging()
        {
            IsStarting = true;
            IsReconnectionRequired = false;
            FolderCheck = new Stopwatch();

            InitChatTimer();
        }

        public void StopLogging()
        {
            if (ChatTimer != null)
            {
                ChatTimer.Dispose();
                ChatTimer = null;
            }

            Disconnect();
        }

        private bool IsStarting;
        #endregion

        #region Timer
        private void InitChatTimer()
        {
            ChatTimer = new Timer(new TimerCallback(ChatTimerCallback));
            ChatTimer.Change(PollDelay, Timeout.InfiniteTimeSpan);
        }

        private void ChatTimerCallback(object state)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new ChatTimerHandler(OnChatTimer));
        }

        private delegate void ChatTimerHandler();
        private void OnChatTimer()
        {
            DateTime Now = DateTime.Now;
            if (LastLogCheck.Day != Now.Day)
            {
                LastLogCheck = Now;
                IsReconnectionRequired = true;
            }
            else if (FolderCheck.Elapsed >= FolderCheckTimeout)
            {
                string OldSelected = SelectedLogFolder;
                SelectFolder();
                if (OldSelected != SelectedLogFolder)
                {
                    IsStarting = true;
                    FolderCheck.Stop();
                    IsReconnectionRequired = true;
                }
                else
                    FolderCheck.Restart();
            }

            if (IsReconnectionRequired)
            {
                IsReconnectionRequired = false;
                Disconnect();
            }

            if (LogStream == null)
            {
                SelectFolder();
                TryConnecting();
            }

            if (LogStream != null)
                ParseChat();

            ChatTimer?.Change(PollDelay, Timeout.InfiniteTimeSpan);
        }

        private bool IsReconnectionRequired;
        private Stopwatch FolderCheck;
        private FileStream LogStream;
        private Timer ChatTimer;
        private DateTime LastLogCheck;
        #endregion

        #region Folders
        private void InitFolders()
        {
            LocalLogFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ProjectGorgon\screenshots");

            string ChatLogPath = NativeMethods.GetKnownFolderPath(NativeMethods.LocalLowId);
            LocalLowLogFolder = Path.Combine(ChatLogPath, @"Elder Game\Project Gorgon\ChatLogs");
        }

        private void SelectFolder()
        {
            if (!string.IsNullOrEmpty(CustomLogFolder))
            {
                SelectedLogFolder = CustomLogFolder;
                return;
            }

            string LocalLogFilePath = FilePathInFolder(LocalLogFolder);
            string LocalLowLogFilePath = FilePathInFolder(LocalLowLogFolder);
            DateTime LocalLastWrite;
            DateTime LocalLowLastWrite;

            if (File.Exists(LocalLogFilePath))
                LocalLastWrite = File.GetLastWriteTimeUtc(LocalLogFilePath);
            else
                LocalLastWrite = DateTime.MinValue;

            if (File.Exists(LocalLowLogFilePath))
                LocalLowLastWrite = File.GetLastWriteTimeUtc(LocalLowLogFilePath);
            else
                LocalLowLastWrite = DateTime.MinValue;

            if (LocalLastWrite >= LocalLowLastWrite)
                SelectedLogFolder = LocalLogFolder;
            else
                SelectedLogFolder = LocalLowLogFolder;
        }

        private void TryConnecting()
        {
            string SelectedLogFilePath = FilePathInFolder(SelectedLogFolder);

            if (File.Exists(SelectedLogFilePath))
            {
                try
                {
                    LogStream = new FileStream(SelectedLogFilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write);
                    if (IsStarting)
                    {
                        IsStarting = false;
                        LogStream.Seek(0, SeekOrigin.End);
                    }

                    FolderCheck.Start();
                }
                catch
                {
                    LogStream = null;
                }
            }
        }

        private static string FilePathInFolder(string LogFolder)
        {
            DateTime Now = DateTime.Now;
            string LogFile = "Chat-" + (Now.Year % 100).ToString(CultureInfo.InvariantCulture) + "-" + Now.Month.ToString("D2", CultureInfo.InvariantCulture) + "-" + Now.Day.ToString("D2", CultureInfo.InvariantCulture) + ".log";
            string LogFilePath = Path.Combine(LogFolder, LogFile);

            return LogFilePath;
        }

        private string CustomLogFolder = null;
        private string SelectedLogFolder = null;
        public string LocalLogFolder { get; private set; }
        public string LocalLowLogFolder { get; private set; }
        #endregion

        #region Parsing
        private void ParseChat()
        {
            long OldPosition = LogStream.Position;
            long NewPosition = LogStream.Length;

            if (NewPosition > OldPosition)
            {
                int Length = (int)(NewPosition - OldPosition);
                byte[] Content = new byte[Length];
                LogStream.Read(Content, 0, Length);

                string ExtractedLines = Encoding.UTF8.GetString(Content);

                string[] Lines = ExtractedLines.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                for (int i = 0; i < Lines.Length; i++)
                {
                    string Line = Lines[i];

                    while (Line.Length > 0 && (Line[Line.Length - 1] == '\r' || Line[Line.Length - 1] == '\n'))
                        Line = Line.Substring(0, Line.Length - 1);

                    Lines[i] = Line;
                }

                for (int i = 0; i < Lines.Length; i++)
                    LogString(Lines[i]);
            }
        }

        private void LogString(string Line)
        {
            if (Line.Length <= 20 || Line[17] != '\t')
                return;

            int Year, Month, Day, Hour, Minute, Second;
            if (!int.TryParse(Line.Substring(0, 2), out Year) ||
                !int.TryParse(Line.Substring(3, 2), out Month) ||
                !int.TryParse(Line.Substring(6, 2), out Day) ||
                !int.TryParse(Line.Substring(9, 2), out Hour) ||
                !int.TryParse(Line.Substring(12, 2), out Minute) ||
                !int.TryParse(Line.Substring(15, 2), out Second))
                return;

            DateTime LogTime = new DateTime(Year, Month, Day, Hour, Minute, Second, DateTimeKind.Local);

            Line = Line.Substring(18);
            ParseLine(LogTime, Line);
        }

        public event NewLineHandler NewLine;

        private void ParseLine(DateTime logTime, string logLine)
        {
            NewLine?.Invoke(this, logTime, logLine);
        }
        #endregion

        #region Implementation of IDisposable
        /// <summary>
        /// Called when an object should release its resources.
        /// </summary>
        /// <param name="isDisposing">Indicates if resources must be disposed now.</param>
        protected virtual void Dispose(bool isDisposing)
        {
            if (!IsDisposed)
            {
                IsDisposed = true;

                if (isDisposing)
                    DisposeNow();
            }
        }

        /// <summary>
        /// Called when an object should release its resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="JsonTextWriter"/> class.
        /// </summary>
        ~Parser()
        {
            Dispose(false);
        }

        /// <summary>
        /// True after <see cref="Dispose(bool)"/> has been invoked.
        /// </summary>
        private bool IsDisposed = false;

        /// <summary>
        /// Disposes of every reference that must be cleaned up.
        /// </summary>
        private void DisposeNow()
        {
            if (ChatTimer != null)
            {
                ChatTimer.Dispose();
                ChatTimer = null;
            }

            if (LogStream != null)
            {
                using (FileStream fs = LogStream) { }
                LogStream = null;
            }
        }
        #endregion
    }
}
