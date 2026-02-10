
// Copyright © 2025 Triamec Motion AG

using Triamec.Tam;
using Triamec.Tam.Motion.TrajectoryStreaming;

namespace TraSt_Sinus {
    internal class StreamingAbortListener {
        public required ITrajectoryStreamer Streaming { get; init; }
        private Thread? _inputThread;
        private bool AbortedByQ { get; set; } = false;
        private bool AbortedByerror { get; set; } = false;

        public bool Exit { get; private set; } = false;
        public bool IsAborted => AbortedByerror || AbortedByQ;
        private bool _listenToExit = false;
        public bool ListenToExit {
            get => _listenToExit;
            set {
                _listenToExit = value;
                if (value == true) { Console.WriteLine("\nPress 'q' or 'ESC' to quit application.\n"); }
            }
        }
        public void AbortByError(Exception ex) {
            AbortedByerror = true;
            Streaming.StopStreaming();
            Console.WriteLine($"\nAborted streaming due to error: {ex.Message}");
        }

        public void Start() {
            _inputThread = new Thread(ListenForAbortKey) {
                IsBackground = true
            };
            _inputThread.Start();
            Console.WriteLine("\nPress 'q' or 'ESC' to abort streaming.\n");
        }

        private void ListenForAbortKey() {
            while (true) {
                var key = Console.ReadKey(intercept: true);
                if ((key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape) && Streaming.IsStreaming && ListenToExit == false && AbortedByQ == false) {
                    Console.WriteLine("\n\nAborted streaming safely by pressing 'q' or 'ESC'.");
                    Streaming.StopStreaming();
                    AbortedByQ = true;
                }
                if((key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape || AbortedByerror) && ListenToExit == true) {
                    Exit = true;
                }
                Thread.Sleep(100);
            }

        }
    }
}
