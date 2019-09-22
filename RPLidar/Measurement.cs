﻿using System;
using System.Collections.Generic;
using System.Text;

namespace RPLidar
{
    /// <summary>
    /// Single measurement
    /// </summary>
    public struct Measurement
    {
        /// <summary>
        /// Is scan new ?
        /// </summary>
        public bool IsNewScan { get; internal set; }

        /// <summary>
        /// Angle in degrees
        /// </summary>
        public float Angle { get; internal set; }

        /// <summary>
        /// Distance in meters
        /// </summary>
        public float Distance { get; internal set; }

        /// <summary>
        /// Reflected signal quality
        /// Only available on legacy scan mode
        /// </summary>
        public int? Quality { get; internal set; }

        /// <summary>
        /// Measurement
        /// </summary>
        /// <param name="isNewScan">Is new scan ?</param>
        /// <param name="angle">Angle in degrees</param>
        /// <param name="distance">Distance in meters</param>
        /// <param name="quality">Reflected signal quality</param>
        public Measurement(bool isNewScan, float angle, float distance, int? quality = null)
        {
            IsNewScan = isNewScan;
            Angle = angle;
            Distance = distance;
            Quality = quality;
        }
    }
}
