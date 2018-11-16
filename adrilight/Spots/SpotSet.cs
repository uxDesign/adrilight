﻿using adrilight.DesktopDuplication;
using adrilight.Extensions;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace adrilight
{
    internal sealed class SpotSet : ISpotSet
    {
        private ILogger _log = LogManager.GetCurrentClassLogger();

        public SpotSet(IUserSettings userSettings)
        {
            UserSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));


            UserSettings.PropertyChanged += (_, e) => DecideRefresh(e.PropertyName);
            Refresh();

            _log.Info($"SpotSet created.");
        }

        private void DecideRefresh(string propertyName)
        {
            switch (propertyName)
            {
                case nameof(UserSettings.BorderDistanceX):
                case nameof(UserSettings.BorderDistanceY):
                case nameof(UserSettings.MirrorX):
                case nameof(UserSettings.MirrorY):
                case nameof(UserSettings.OffsetLed):
                case nameof(UserSettings.SpotHeight):
                case nameof(UserSettings.SpotsX):
                case nameof(UserSettings.SpotsY):
                case nameof(UserSettings.SpotWidth):
                    Refresh();
                    break;
            }
        }

        public ISpot[] Spots { get; set; }

        public object Lock { get; } = new object();

        /// <summary>
        /// returns the number of leds
        /// </summary>
        public static int CountLeds(int spotsX, int spotsY)
        {
            return 2 * spotsX + 2 * spotsY;
        }

        public int ExpectedScreenWidth => Screen.PrimaryScreen.Bounds.Width / DesktopDuplicator.ScalingFactor;
        public int ExpectedScreenHeight => Screen.PrimaryScreen.Bounds.Height / DesktopDuplicator.ScalingFactor;

        private IUserSettings UserSettings { get; }


        private void Refresh()
        {
            lock (Lock)
            {
                Spots = BuildSpots(ExpectedScreenWidth, ExpectedScreenHeight, UserSettings);
            }
        }

        internal static IEnumerable<(int x, int y)> BoundsWalker(int horizontalStripCount, int verticalStripCount)
        {
            if (horizontalStripCount < 1) throw new ArgumentOutOfRangeException(nameof(horizontalStripCount));
            if (verticalStripCount < 1) throw new ArgumentOutOfRangeException(nameof(verticalStripCount));

            /* counting direction is clockwise:
             * 
             *    0123
             *    9  4
             *    8765
             * 
             * number of expected entries = 2*horizontalStripCount + 2*verticalStripCount
             * 
             * ranges are 
             * 1..horizontalStripCount, 0  = top
             * horizontalStripCount+1, 1..verticalStripCount  = right
             * horizontalStripCount..1, verticalStripCount+1  = bottom
             * 0, verticalStripCount..1)  = left
             */

             //top
            for (int x = 1; x <= horizontalStripCount; x++)
            {
                yield return (x, 0);
            }

            //right
            for (int y = 1; y <= verticalStripCount; y++)
            {
                yield return (horizontalStripCount + 1, y);
            }

            //bottom
            for (int x = horizontalStripCount; x >= 1; x--)
            {
                yield return (x, verticalStripCount+1);
            }

            //left
            for (int y = verticalStripCount; y >=1; y--)
            {
                yield return (0, y);
            }
        }

        internal static ISpot[] BuildSpots(int screenWidth, int screenHeight, IUserSettings userSettings)
        {
            var spotsX = userSettings.SpotsX;
            var spotsY = userSettings.SpotsY;
            ISpot[] spots = new Spot[CountLeds(spotsX, spotsY)];


            var scalingFactor = DesktopDuplicator.ScalingFactor;
            var borderDistanceX = userSettings.BorderDistanceX / scalingFactor;
            var borderDistanceY = userSettings.BorderDistanceY / scalingFactor;
            var spotWidth = userSettings.SpotWidth / scalingFactor;
            var spotHeight = userSettings.SpotHeight / scalingFactor;

            var counter = 0;
            var relationIndex = spotsX - spotsY + 1;

            for (var j = 0; j < spotsY; j++)
            {
                for (var i = 0; i < spotsX; i++)
                {
                    var isFirstColumn = i == 0;
                    var isLastColumn = i == spotsX - 1;
                    var isFirstRow = j == 0;
                    var isLastRow = j == spotsY - 1;

                    if (isFirstColumn || isLastColumn || isFirstRow || isLastRow) // needing only outer spots
                    {
                        var x = ((spotsX > 1 ? ((screenWidth - 2 * borderDistanceX - spotWidth) % (spotsX - 1)) / 2 : 0) + borderDistanceX + i * (spotsX > 1 ? (screenWidth - 2 * borderDistanceX - spotWidth) / (spotsX - 1) : 0))
                                .Clamp(0, screenWidth);

                        var y = ((spotsY > 1 ? ((screenHeight - 2 * borderDistanceY - spotHeight) % (spotsY - 1)) / 2 : 0) + borderDistanceY  + j * (spotsY > 1 ? (screenHeight - 2 * borderDistanceY - spotHeight) / (spotsY - 1) : 0))
                                .Clamp(0, screenHeight);

                        var index = counter++; // in first row index is always counter

                        if (spotsX > 1 && spotsY > 1)
                        {
                            if (!isFirstRow && !isLastRow)
                            {
                                if (isFirstColumn)
                                {
                                    index += relationIndex + ((spotsY - 1 - j) * 3);
                                }
                                else if (isLastColumn)
                                {
                                    index -= j;
                                }
                            }

                            if (!isFirstRow && isLastRow)
                            {
                                index += relationIndex - i * 2;
                            }
                        }

                        spots[index] = new Spot(x, y, spotWidth, spotHeight);
                    }
                }
            }

            //TODO totally broken :(

            if (userSettings.OffsetLed != 0) Offset(ref spots, userSettings.OffsetLed);
            if (spotsY > 1 && userSettings.MirrorX) MirrorX(spots, spotsX, spotsY);
            if (spotsX > 1 && userSettings.MirrorY) MirrorY(spots, spotsX, spotsY);

            spots[0].IsFirst = true;
            return spots;
        }

        private static void Mirror(ISpot[] spots, int startIndex, int length)
        {
            var halfLength = (length/2);
            var endIndex = startIndex + length - 1;

            for (var i = 0; i < halfLength; i++)
            {
                spots.Swap(startIndex + i, endIndex - i);
            }
        }

        private static void MirrorX(ISpot[] spots, int spotsX, int spotsY)
        {
            // copy swap last row to first row inverse
            for (var i = 0; i < spotsX; i++)
            {
                var index1 = i;
                var index2 = (spots.Length - 1) - (spotsY - 2) - i;
                spots.Swap(index1, index2);
            }

            // mirror first column
            Mirror(spots, spotsX, spotsY - 2);

            // mirror last column
            if (spotsX > 1)
                Mirror(spots, 2 * spotsX + spotsY - 2, spotsY - 2);
        }

        private static void MirrorY(ISpot[] spots, int spotsX, int spotsY)
        {
            // copy swap last row to first row inverse
            for (var i = 0; i < spotsY - 2; i++)
            {
                var index1 = spotsX + i;
                var index2 = (spots.Length - 1) - i;
                spots.Swap(index1, index2);
            }

            // mirror first row
            Mirror(spots, 0, spotsX);

            // mirror last row
            if (spotsY > 1)
                Mirror(spots, spotsX + spotsY - 2, spotsX);
        }

        private static void Offset(ref ISpot[] spots, int offset)
        {
            ISpot[] temp = new Spot[spots.Length];
            for (var i = 0; i < spots.Length; i++)
            {
                temp[(i + temp.Length + offset)%temp.Length] = spots[i];
            }
            spots = temp;
        }

        public void IndicateMissingValues()
        {
            foreach (var spot in Spots)
            {
                spot.IndicateMissingValue();
            }
        }
    }


}
