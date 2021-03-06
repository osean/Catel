﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="IPhonePage.cs" company="Catel development team">
//   Copyright (c) 2008 - 2014 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Catel.MVVM.Views
{
    using System;
    using MVVM;

    /// <summary>
    /// Interface defining functionality for a phone page.
    /// </summary>
    public interface IPhonePage : INavigationView
    {
        /// <summary>
        /// Gets or sets a value indicating whether the back key cancels the view model. This
        /// means that <see cref="IViewModel.CancelViewModel"/> will be called when the back key is pressed.
        /// <para/>
        /// If this property is <c>false</c>, the <see cref="IViewModel.SaveViewModel"/> will be called instead.
        /// <para/>
        /// Default value is <c>true</c>.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if the back key cancels the view model; otherwise, <c>false</c>.
        /// </value>
        bool BackKeyCancelsViewModel { get; set; }

        /// <summary>
        /// Occurs when the back key is pressed.
        /// </summary>
        event EventHandler<EventArgs> BackKeyPress;
    }
}
