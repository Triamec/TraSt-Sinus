
// Copyright © 2025 Triamec Motion AG

namespace TraSt_Sinus {
    internal class SineSegmentGenerator {
        private bool _isLastSegment = false;
        private readonly int _maxRowsPerSegment;
        private readonly double _duration;
        private readonly double _streamRate;
        private readonly int _numTotalColumns;

        private readonly int _numTotalRows;
        private readonly int _numTotalSegments;
        private readonly double _timeBetweenPoints;

        private int _currentSegmentIndex = 0;
        public SineSegmentGenerator(int maxPointsPerSegment, double duration, double streamRate, int numTotalColumns) {
            _maxRowsPerSegment = maxPointsPerSegment;
            _duration = duration;
            _streamRate = streamRate;
            _numTotalColumns = numTotalColumns;

            _numTotalRows = (int)(_duration * _streamRate);
            _numTotalSegments = (int)Math.Ceiling((double)_numTotalRows / _maxRowsPerSegment);
            _timeBetweenPoints = 1.0 / _streamRate;
        }

        internal double[,] GenerateSegment() {

            int rowsInThisSegment = _maxRowsPerSegment;

            if (_currentSegmentIndex == _numTotalSegments - 1) {
                _isLastSegment = true;                                                              // check if this is the last segment
                if (_numTotalRows % _maxRowsPerSegment != 0) {
                    rowsInThisSegment = _numTotalRows % _maxRowsPerSegment;                         // if it is the last segment, calculate how many rows are left and adapt buffer size
                }
            } else { _isLastSegment = false; }


            double[,] segmentData = new double[rowsInThisSegment, _numTotalColumns];                // create a buffer for the segment data with the number of rows and columns


            for (int i = 0; i < _maxRowsPerSegment; i++) {                                          // iterate through all the rows in the segment
                int globalRowIndex = _currentSegmentIndex * _maxRowsPerSegment + i;
                double t = globalRowIndex * _timeBetweenPoints;
                double value = Math.Sin(2 * Math.PI * t);                                           // calculate the sine value for the current time point

                for (int j = 0; j < _numTotalColumns; j++) {                                        // iterate through all the columns in the segment
                    segmentData[i, j] = value;
                }
            }
            _currentSegmentIndex++;
            return segmentData;
        }

        public bool HasMoreData() { return !_isLastSegment; }
    }
}
