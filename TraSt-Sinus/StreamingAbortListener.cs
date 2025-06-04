
// Copyright © 2025 Triamec Motion AG

using Triamec.Tam;

namespace TraSt_Sinus {
    internal class StreamingAbortListener {
        public required TrajectoryStreaming Streaming { get; init; }
        private Thread? _inputThread;
        private bool AbortedByQ { get; set; } = false;
        private bool AbortedByerror { get; set; } = false;

        public bool IsAborted => AbortedByerror || AbortedByQ;

        public void AbortByError(Exception ex) {
            AbortedByerror = true;
            Streaming.Stop();
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
            while (!IsAborted) {
                var key = Console.ReadKey(intercept: true);
                if ((key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape) && Streaming.IsStreaming) {
                    Console.WriteLine("\n\nAborted streaming safely by pressing 'q' or 'ESC'.");
                    Streaming.Stop();
                    AbortedByQ = true;
                    break;
                }
                Thread.Sleep(100);
            }
        }
    }
}
