﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="VibrateService.android.cs" company="Catel development team">
//   Copyright (c) 2008 - 2014 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

#if ANDROID

namespace Catel.Services
{
    using System;
    using global::Android.App;
    using global::Android.Content;
    using global::Android.OS;

    public partial class VibrateService
    {
        private Vibrator _vibrator;

        partial void Initialize()
        {
            var context = Application.Context;
            _vibrator = context.GetSystemService(Context.VibratorService) as Vibrator;
        }

        partial void StartVibration(TimeSpan duration)
        {
            if (_vibrator != null)
            {
                _vibrator.Vibrate((long)duration.TotalMilliseconds);
            }
        }

        partial void StopVibration()
        {
            if (_vibrator != null)
            {
                _vibrator.Cancel();
            }
        }
    }
}

#endif