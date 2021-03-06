﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PhoneApplicationPage.cs" company="Catel development team">
//   Copyright (c) 2008 - 2014 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Catel.Phone.Controls
{
    using System;
    using System.ComponentModel;
    using System.Windows;
    using Catel.MVVM.Providers;
    using Catel.MVVM.Views;
    using Catel.Windows;
    using IoC;
    using Logging;
    using MVVM;
    using Windows.Data;

    /// <summary>
    /// <see cref="PhoneApplicationPage"/> class that supports MVVM with Catel.
    /// </summary>
    /// <remarks>
    /// This control can resolve a view model in the following ways:<para />
    /// <list type="numbered">
    ///   <item>
    ///     <description>By using the <see cref="GetViewModelType()"/> method.</description>
    ///   </item>
    ///   <item>
    ///     <description>By using the <see cref="IViewModelLocator"/> which is registered in the <see cref="IServiceLocator"/>.</description>
    ///   </item>
    /// </list>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The view model of the view could not be resolved. Use either the <see cref="GetViewModelType()"/> method or <see cref="IViewModelLocator"/>.</exception>
    public class PhoneApplicationPage : Microsoft.Phone.Controls.PhoneApplicationPage, IPhonePage
    {
        #region Fields
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        private static readonly IViewModelLocator _viewModelLocator;

        private readonly PhonePageLogic _logic;

        private event EventHandler<EventArgs> _backKeyPress;
        private event EventHandler<EventArgs> _viewLoaded;
        private event EventHandler<EventArgs> _viewUnloaded;
        private event EventHandler<EventArgs> _viewDataContextChanged;
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes static members of the <see cref="PhoneApplicationPage"/> class.
        /// </summary>
        static PhoneApplicationPage()
        {
            var dependencyResolver = IoCConfiguration.DefaultDependencyResolver;

            _viewModelLocator = dependencyResolver.Resolve<IViewModelLocator>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PhoneApplicationPage"/> class.
        /// </summary>
        /// <remarks>
        /// It is not possible to inject view models (actually, you can't even
        /// pass view models during navigation in Windows Phone 7).
        /// </remarks>
        public PhoneApplicationPage()
        {
            if (CatelEnvironment.IsInDesignMode)
            {
                return;
            }

            var viewModelType = GetViewModelType();
            if (viewModelType == null)
            {
                Log.Debug("GetViewModelType() returned null, using the ViewModelLocator to resolve the view model");

                viewModelType = _viewModelLocator.ResolveViewModel(GetType());
                if (viewModelType == null)
                {
                    const string error = "The view model of the view could not be resolved. Use either the GetViewModelType() method or IViewModelLocator";
                    Log.Error(error);
                    throw new NotSupportedException(error);
                }
            }

            _logic = new PhonePageLogic(this, viewModelType);
            _logic.TargetViewPropertyChanged += (sender, e) =>
            {
                OnPropertyChanged(e);

                PropertyChanged.SafeInvoke(this, e);
            };

            _logic.ViewModelChanged += (sender, e) => RaiseViewModelChanged();

            _logic.ViewModelPropertyChanged += (sender, e) =>
            {
                OnViewModelPropertyChanged(e);

                ViewModelPropertyChanged.SafeInvoke(this, e);
            };

            _logic.DetermineViewModelInstance += (sender, e) =>
            {
                e.ViewModel = GetViewModelInstance(e.DataContext);
            };

            _logic.DetermineViewModelType += (sender, e) =>
            {
                e.ViewModelType = GetViewModelType(e.DataContext);
            };

            _logic.ViewLoading += (sender, e) => ViewLoading.SafeInvoke(this);
            _logic.ViewLoaded += (sender, e) => ViewLoaded.SafeInvoke(this);
            _logic.ViewUnloading += (sender, e) => ViewUnloading.SafeInvoke(this);
            _logic.ViewUnloaded += (sender, e) => ViewUnloaded.SafeInvoke(this);

            _logic.Tombstoning += (sender, e) => OnTombstoning();
            _logic.Tombstoned += (sender, e) => OnTombstoned();
            _logic.RecoveringFromTombstoning += (sender, e) => OnRecoveringFromTombstoning();
            _logic.RecoveredFromTombstoning += (sender, e) => OnRecoveredFromTombstoning();

            Loaded += (sender, e) =>
            {
                OnLoaded(e);

                _viewLoaded.SafeInvoke(this);
            };

            Unloaded += (sender, e) =>
            {
                OnUnloaded(e);

                _viewUnloaded.SafeInvoke(this);
            };

            BackKeyPress += (sender, e) => _backKeyPress.SafeInvoke(this);
            this.AddDataContextChangedHandler((sender, e) => _viewDataContextChanged.SafeInvoke(this));
        }
        #endregion

        #region Properties
        /// <summary>
        /// Gets the type of the view model that this user control uses.
        /// </summary>
        public Type ViewModelType
        {
            get { return _logic.GetValue<PhonePageLogic, Type>(x => x.ViewModelType); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the view model container should prevent the 
        /// creation of a view model.
        /// <para />
        /// This property is very useful when using views in transitions where the view model is no longer required.
        /// </summary>
        /// <value><c>true</c> if the view model container should prevent view model creation; otherwise, <c>false</c>.</value>
        public bool PreventViewModelCreation
        {
            get { return _logic.GetValue<PhonePageLogic, bool>(x => x.PreventViewModelCreation); }
            set { _logic.SetValue<PhonePageLogic>(x => x.PreventViewModelCreation = value); }
        }

        /// <summary>
        /// Gets the view model that is contained by the container.
        /// </summary>
        /// <value>The view model.</value>
        public IViewModel ViewModel
        {
            get { return _logic.GetValue<PhonePageLogic, IViewModel>(x => x.ViewModel); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether navigating away from the page should save the view model.
        /// <para />
        /// The default value is <c>true</c>.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if navigating away should save the view model; otherwise, <c>false</c>.
        /// </value>
        public bool NavigatingAwaySavesViewModel
        {
            get { return _logic.GetValue<PhonePageLogic, bool>(x => x.NavigatingAwaySavesViewModel, true); }
            set { _logic.SetValue<PhonePageLogic>(x => x.NavigatingAwaySavesViewModel = value); }
        }

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
        public bool BackKeyCancelsViewModel
        {
            get { return _logic.GetValue<PhonePageLogic, bool>(x => x.BackKeyCancelsViewModel, true); }
            set { _logic.SetValue<PhonePageLogic>(x => x.BackKeyCancelsViewModel = value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the view model should be closed when navigating forward.
        /// <para />
        /// By default, Catel will keep the view models and pages in memory to provide a back-navigation stack. Some
        /// pages are not required to be listed in the navigation stack and can have this property set to <c>true</c>.
        /// <para />
        /// The default value is <c>false</c>.
        /// </summary>
        /// <value><c>true</c> if the view modle must be closed on forward navigation; otherwise, <c>false</c>.</value>
        public bool CloseViewModelOnForwardNavigation
        {
            get { return _logic.GetValue<PhonePageLogic, bool>(x => x.CloseViewModelOnForwardNavigation, true); }
            set { _logic.SetValue<PhonePageLogic>(x => x.CloseViewModelOnForwardNavigation = value); }
        }

        /// <summary>
        /// Gets the parent of the view.
        /// </summary>
        /// <value>The parent.</value>
        object IView.Parent
        {
            get { return Parent; }
        }
        #endregion

        #region Events
        /// <summary>
        /// Occurs when a property on the container has changed.
        /// </summary>
        /// <remarks>
        /// This event makes it possible to externally subscribe to property changes of a <see cref="DependencyObject"/>
        /// (mostly the container of a view model) because the .NET Framework does not allows us to.
        /// </remarks>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Occurs when the <see cref="ViewModel"/> property has changed.
        /// </summary>
        public event EventHandler<EventArgs> ViewModelChanged;

        /// <summary>
        /// Occurs when a property on the <see cref="ViewModel"/> has changed.
        /// </summary>
        public event EventHandler<PropertyChangedEventArgs> ViewModelPropertyChanged;

        /// <summary>
        /// Occurs when the view model container is loading.
        /// </summary>
        public event EventHandler<EventArgs> ViewLoading;

        /// <summary>
        /// Occurs when the view model container is loaded.
        /// </summary>
        public event EventHandler<EventArgs> ViewLoaded;

        /// <summary>
        /// Occurs when the view model container starts unloading.
        /// </summary>
        public event EventHandler<EventArgs> ViewUnloading;

        /// <summary>
        /// Occurs when the view model container is unloaded.
        /// </summary>
        public event EventHandler<EventArgs> ViewUnloaded;

        /// <summary>
        /// Occurs when the back key is pressed.
        /// </summary>
        event EventHandler<EventArgs> IPhonePage.BackKeyPress
        {
            add { _backKeyPress += value; }
            remove { _backKeyPress -= value; }
        }

        /// <summary>
        /// Occurs when the view is loaded.
        /// </summary>
        event EventHandler<EventArgs> IView.Loaded
        {
            add { _viewLoaded += value; }
            remove { _viewLoaded -= value; }
        }

        /// <summary>
        /// Occurs when the view is unloaded.
        /// </summary>
        event EventHandler<EventArgs> IView.Unloaded
        {
            add { _viewUnloaded += value; }
            remove { _viewUnloaded -= value; }
        }

        /// <summary>
        /// Occurs when the data context has changed.
        /// </summary>
        event EventHandler<EventArgs> IView.DataContextChanged
        {
            add { _viewDataContextChanged += value; }
            remove { _viewDataContextChanged -= value; }
        }
        #endregion

        #region Methods
        private void RaiseViewModelChanged()
        {
            OnViewModelChanged();

            ViewModelChanged.SafeInvoke(this);
            PropertyChanged.SafeInvoke(this, new PropertyChangedEventArgs("ViewModel"));
        }

        /// <summary>
        /// Gets the type of the view model. If this method returns <c>null</c>, the view model type will be retrieved by naming 
        /// convention using the <see cref="IViewModelLocator"/> registered in the <see cref="IServiceLocator"/>.
        /// </summary>
        /// <returns>The type of the view model or <c>null</c> in case it should be auto determined.</returns>
        protected virtual Type GetViewModelType()
        {
            return null;
        }

        /// <summary>
        /// Gets the type of the view model at runtime based on the <see cref="FrameworkElement.DataContext"/>. If this method returns 
        /// <c>null</c>, the earlier determined view model type will be used instead.
        /// </summary>
        /// <param name="dataContext">The data context. This value can be <c>null</c>.</param>
        /// <returns>The type of the view model or <c>null</c> in case it should be auto determined.</returns>
        /// <remarks>
        /// Note that this method is only called when the <see cref="FrameworkElement.DataContext"/> changes.
        /// </remarks>
        protected virtual Type GetViewModelType(object dataContext)
        {
            return null;
        }

        /// <summary>
        /// Gets the instance of the view model at runtime based on the <see cref="FrameworkElement.DataContext"/>. If this method returns 
        /// <c>null</c>, the logic will try to construct the view model by itself.
        /// </summary>
        /// <param name="dataContext">The data context. This value can be <c>null</c>.</param>
        /// <returns>The instance of the view model or <c>null</c> in case it should be auto created.</returns>
        /// <remarks>
        /// Note that this method is only called when the <see cref="FrameworkElement.DataContext"/> changes.
        /// </remarks>
        protected virtual IViewModel GetViewModelInstance(object dataContext)
        {
            return null;
        }

        /// <summary>
        /// Called when the page is loaded.
        /// </summary>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected virtual void OnLoaded(EventArgs e)
        {
        }

        /// <summary>
        /// Called when the page is unloaded.
        /// </summary>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected virtual void OnUnloaded(EventArgs e)
        {
        }

        /// <summary>
        /// Called when a dependency property on this control has changed.
        /// </summary>
        /// <param name="e">The <see cref="PropertyChangedEventArgs"/> instance containing the event data.</param>
        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
        }

        /// <summary>
        /// Called when a property on the current <see cref="ViewModel"/> has changed.
        /// </summary>
        /// <param name="e">The <see cref="System.ComponentModel.PropertyChangedEventArgs"/> instance containing the event data.</param>
        protected virtual void OnViewModelPropertyChanged(PropertyChangedEventArgs e)
        {
        }

        /// <summary>
        /// Called when the <see cref="ViewModel"/> has changed.
        /// </summary>
        /// <remarks>
        /// This method does not implement any logic and saves a developer from subscribing/unsubscribing
        /// to the <see cref="ViewModelChanged"/> event inside the same user control.
        /// </remarks>
        protected virtual void OnViewModelChanged()
        {
        }

        /// <summary>
        /// Validates the data.
        /// </summary>
        /// <returns>True if successful, otherwise false.</returns>
        protected bool ValidateData()
        {
            return _logic.ValidateViewModel();
        }

        /// <summary>
        /// Discards all changes made by this window.
        /// </summary>
        protected void DiscardChanges()
        {
            _logic.CancelViewModel();
        }

        /// <summary>
        /// Applies all changes made by this window.
        /// </summary>
        /// <returns>True if successful, otherwise false.</returns>
        protected bool ApplyChanges()
        {
            return _logic.SaveViewModel();
        }

        /// <summary>
        /// Called when the view is about to tombstone itself.
        /// </summary>
        /// <remarks>
        /// Note that it is not required to call the base.
        /// </remarks>
        protected virtual void OnTombstoning()
        {
        }

        /// <summary>
        /// Called when the view has just tombstoned itself.
        /// </summary>
        /// <remarks>
        /// Note that it is not required to call the base.
        /// </remarks>
        protected virtual void OnTombstoned()
        {
        }

        /// <summary>
        /// Called when the view is about to recover itself from a tombstoned state.
        /// </summary>
        /// <remarks>
        /// Note that it is not required to call the base.
        /// </remarks>
        protected virtual void OnRecoveringFromTombstoning()
        {
        }

        /// <summary>
        /// Called when the view has just recovered itself from a tombstoned state.
        /// </summary>
        /// <remarks>
        /// Note that it is not required to call the base.
        /// </remarks>
        protected virtual void OnRecoveredFromTombstoning()
        {
        }
        #endregion
    }
}