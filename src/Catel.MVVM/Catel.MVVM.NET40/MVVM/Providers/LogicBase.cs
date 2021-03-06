﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="LogicBase.cs" company="Catel development team">
//   Copyright (c) 2008 - 2014 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Catel.MVVM.Providers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;

    using Catel.Caching;
    using Catel.Data;
    using IoC;
    using Logging;
    using MVVM;
    using MVVM.Views;
    using Reflection;

    /// <summary>
    /// Available unload behaviors.
    /// </summary>
    public enum UnloadBehavior
    {
        /// <summary>
        /// Closes the view model.
        /// </summary>
        CloseViewModel,

        /// <summary>
        /// Saves and closes the view model.
        /// </summary>
        SaveAndCloseViewModel,

        /// <summary>
        /// Cancels and closes the view model.
        /// </summary>
        CancelAndCloseViewModel
    }

    /// <summary>
    /// Available view load state events.
    /// </summary>
    public enum ViewLoadStateEvent
    {
        /// <summary>
        /// The view is about to be loaded.
        /// </summary>
        Loading,

        /// <summary>
        /// The view has just been loaded.
        /// </summary>
        Loaded,

        /// <summary>
        /// The view is about to be unloaded.
        /// </summary>
        Unloading,

        /// <summary>
        /// The view has just been unloaded.
        /// </summary>
        Unloaded
    }

    /// <summary>
    /// The available view model behaviors.
    /// </summary>
    public enum LogicViewModelBehavior
    {
        /// <summary>
        /// View model was injected thus will be stable during the lifetime of the view.
        /// </summary>
        Injected,

        /// <summary>
        /// View model is dynamic and will be automatically determined.
        /// </summary>
        Dynamic
    }

    /// <summary>
    /// Base implementation of the behaviors, which defines all the different possible situations
    /// a behavior must implement / support to be a valid MVVM provider behavior.
    /// </summary>
    public abstract class LogicBase : ObservableObject
    {
        #region Fields
        /// <summary>
        /// The log.
        /// </summary>
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The view model factory.
        /// </summary>
        private static readonly IViewModelFactory _viewModelFactory;

        /// <summary>
        /// The view manager.
        /// </summary>
        private static readonly IViewManager _viewManager;

        /// <summary>
        /// The view property selector.
        /// </summary>
        private static readonly IViewPropertySelector _viewPropertySelector;

        /// <summary>
        /// A list of dependency properties to subscribe to per type.
        /// </summary>
        private static readonly ICacheStorage<Type, List<string>> _viewPropertiesToSubscribe = new CacheStorage<Type, List<string>>();

        /// <summary>
        /// The view model instances currently held by this provider. This value should only be used
        /// inside the <see cref="ViewModel"/> property. For accessing the view model, use the 
        /// <see cref="ViewModel"/> property.
        /// </summary>
        private IViewModel _viewModel;

        /// <summary>
        /// Boolean representing whether this is the first validation after the control has been loaded.
        /// </summary>
        private bool _isFirstValidationAfterLoaded = true;

        /// <summary>
        /// The last invoked view load state event.
        /// </summary>
        private ViewLoadStateEvent _lastInvokedViewLoadStateEvent;

        /// <summary>
        /// The view loaded manager.
        /// </summary>
        private static readonly IViewLoadedManager _viewLoadedManager;
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes static members of the <see cref="LogicBase"/> class.
        /// </summary>
        static LogicBase()
        {
            var dependencyResolver = IoCConfiguration.DefaultDependencyResolver;

            _viewModelFactory = dependencyResolver.Resolve<IViewModelFactory>();
            _viewManager = dependencyResolver.Resolve<IViewManager>();
            _viewPropertySelector = dependencyResolver.Resolve<IViewPropertySelector>();
            _viewLoadedManager = dependencyResolver.Resolve<IViewLoadedManager>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogicBase"/> class.
        /// </summary>
        /// <param name="targetView">The target control.</param>
        /// <param name="viewModelType">Type of the view model.</param>
        /// <param name="viewModel">The view model.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="targetView"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="viewModelType"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="viewModelType"/> does not implement interface <see cref="IViewModel"/>.</exception>
        protected LogicBase(IView targetView, Type viewModelType, IViewModel viewModel = null)
        {
            Argument.IsNotNull("targetView", targetView);
            Argument.IsNotNull("viewModelType", viewModelType);
            Argument.ImplementsInterface("viewModelType", viewModelType, typeof(IViewModel));

            UniqueIdentifier = UniqueIdentifierHelper.GetUniqueIdentifier<LogicBase>();

            Log.Debug("Constructing behavior '{0}' for '{1}' with unique id '{2}'", GetType().Name, targetView.GetType().Name, UniqueIdentifier);

            TargetView = targetView;
            ViewModelType = viewModelType;
            ViewModel = viewModel;

#if SL4 || SL5
            Catel.Windows.FrameworkElementExtensions.FixUILanguageBug((System.Windows.FrameworkElement)TargetView);
#endif

            ViewModelBehavior = (viewModel != null) ? LogicViewModelBehavior.Injected : LogicViewModelBehavior.Dynamic;

            if (ViewModel != null)
            {
                SetDataContext(ViewModel);
            }

            // Very impoortant to exit here in design mode
            if (CatelEnvironment.IsInDesignMode)
            {
                return;
            }

            _viewLoadedManager.AddView(TargetView, () => OnTargetViewLoadedInternal(TargetView, EventArgs.Empty));

            TargetView.Unloaded += OnTargetViewUnloadedInternal;
            TargetView.DataContextChanged += OnTargetViewDataContextChanged;

            // This also subscribes to DataContextChanged, don't double subscribe
            var viewPropertiesToSubscribe = DetermineInterestingViewProperties();
            foreach (var viewPropertyToSubscribe in viewPropertiesToSubscribe)
            {
                TargetView.SubscribeToPropertyChanged(viewPropertyToSubscribe, OnTargetViewPropertyChanged);
            }

            Log.Debug("Constructed behavior '{0}' for '{1}'", GetType().Name, TargetView.GetType().Name);
        }
        #endregion

        #region Properties
        /// <summary>
        /// Gets the view model factory used to create the view model instances.
        /// </summary>
        protected IViewModelFactory ViewModelFactory
        {
            get { return _viewModelFactory; }
        }

        /// <summary>
        /// Gets or sets the view model.
        /// </summary>
        /// <value>The view model.</value>
        /// <remarks>
        /// When a new value is set, the old view model will be disposed.
        /// </remarks>
        public IViewModel ViewModel
        {
            get
            {
                return _viewModel;
            }
            protected set
            {
                if (_viewModel == value)
                {
                    return;
                }

                OnViewModelChanging();

                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                    _viewModel.Canceled -= OnViewModelCanceled;
                    _viewModel.Saved -= OnViewModelSaved;
                    _viewModel.Closed -= OnViewModelClosed;
                }

                _viewModel = value;

                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                    _viewModel.Canceled += OnViewModelCanceled;
                    _viewModel.Saved += OnViewModelSaved;
                    _viewModel.Closed += OnViewModelClosed;

                    // Must be in a try/catch because Silverlight sometimes throws out of range exceptions for bindings
                    try
                    {
                        SetDataContext(_viewModel);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Caught exception while setting DataContext to new value", ex);
                    }
                }

                OnViewModelChanged();

                ViewModelChanged.SafeInvoke(this);

                RaisePropertyChanged("ViewModel");

                if ((_viewModel != null) && IsTargetViewLoaded)
                {
                    _viewModel.InitializeViewModel();
                }
            }
        }

        /// <summary>
        /// Gets the unique identifier.
        /// </summary>
        /// <value>The unique identifier.</value>
        public int UniqueIdentifier { get; private set; }

        /// <summary>
        /// Gets the view model behavior.
        /// </summary>
        /// <value>The view model behavior.</value>
        public LogicViewModelBehavior ViewModelBehavior { get; private set; }

        /// <summary>
        /// Gets the type of the view model.
        /// </summary>
        /// <value>The type of the view model.</value>
        public Type ViewModelType { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether the view model container should prevent the 
        /// creation of a view model.
        /// <para />
        /// This property is very useful when using views in transitions where the view model is no longer required.
        /// </summary>
        /// <value><c>true</c> if the view model container should prevent view model creation; otherwise, <c>false</c>.</value>
        public bool PreventViewModelCreation { get; set; }

        /// <summary>
        /// Gets the target control of this MVVM provider.
        /// </summary>
        /// <value>The target control.</value>
        protected internal IView TargetView { get; set; }

        /// <summary>
        /// Gets the type of the target control.
        /// </summary>
        /// <value>The type of the target control.</value>
        protected Type TargetViewType
        {
            get { return TargetView.GetType(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether a <c>null</c> DataContext should be ignored and no new view
        /// model should be created.
        /// <para />
        /// This property will automatically be set to <c>true</c> when a parent view model container invokes the
        /// <see cref="IViewModelContainer.ViewUnloading"/> event. It will be set to <c>false</c> again when the parent
        /// view model container invokes the <see cref="IViewModelContainer.ViewLoading"/>.
        /// <para />
        /// The default value is <c>false</c>.
        /// </summary>
        /// <value><c>true</c> if the <c>null</c> DataContext should be ignored; otherwise, <c>false</c>.</value>
        protected bool IgnoreNullDataContext { get; set; }

        /// <summary>
        /// Gets a value indicating whether the target control is loaded or not.
        /// </summary>
        /// <value>
        /// <c>true</c> if the target control is loaded; otherwise, <c>false</c>.
        /// </value>
        public bool IsTargetViewLoaded { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the control can be loaded. This is very useful in non-WPF classes where
        /// the <c>LayoutUpdated</c> is used instead of the <c>Loaded</c> event.
        /// <para />
        /// If this value is <c>true</c>, this logic implementation can call the <see cref="OnTargetViewLoaded"/> when
        /// the control is loaded. Otherwise, the call will be ignored.
        /// </summary>
        /// <remarks>
        /// This value is introduced for Windows Phone because a navigation backwards still leads to a call to
        /// <c>LayoutUpdated</c>. To prevent new view models from being created, this property can be overridden by 
        /// such logic implementations.
        /// </remarks>
        /// <value><c>true</c> if this instance can control be loaded; otherwise, <c>false</c>.</value>
        protected virtual bool CanViewBeLoaded { get { return true; } }

        /// <summary>
        /// Gets a value indicating whether this instance is loading.
        /// </summary>
        /// <value><c>true</c> if this instance is loading; otherwise, <c>false</c>.</value>
        protected bool IsLoading { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance is unloading.
        /// </summary>
        /// <value><c>true</c> if this instance is unloading; otherwise, <c>false</c>.</value>
        protected bool IsUnloading { get; private set; }
        #endregion

        #region Events
        /// <summary>
        /// Occurs when the view model is about to construct a new view model. This event can be used to
        /// intercept and inject a dynamically instantiated view model.
        /// </summary>
        public event EventHandler<DetermineViewModelInstanceEventArgs> DetermineViewModelInstance;

        /// <summary>
        /// Occurs when the view model is about to construct a new view model. This event can be used to
        /// intercept and inject a dynamically determined view model type.
        /// </summary>
        public event EventHandler<DetermineViewModelTypeEventArgs> DetermineViewModelType;

        /// <summary>
        /// Occurs when the <see cref="ViewModel"/> property has changed.
        /// </summary>
        public event EventHandler<EventArgs> ViewModelChanged;

        /// <summary>
        /// Occurs when a property on the current <see cref="ViewModel"/> has changed.
        /// </summary>
        public event EventHandler<PropertyChangedEventArgs> ViewModelPropertyChanged;

        /// <summary>
        /// Occurs when the <see cref="ViewModel"/> has been canceled.
        /// </summary>
        public event EventHandler<EventArgs> ViewModelCanceled;

        /// <summary>
        /// Occurs when the <see cref="ViewModel"/> has been saved.
        /// </summary>
        public event EventHandler<EventArgs> ViewModelSaved;

        /// <summary>
        /// Occurs when the <see cref="ViewModel"/> has been closed.
        /// </summary>
        public event EventHandler<ViewModelClosedEventArgs> ViewModelClosed;

        /// <summary>
        /// Occurs when a property on the <see cref="TargetView"/> has changed.
        /// </summary>
        public event EventHandler<PropertyChangedEventArgs> TargetViewPropertyChanged;

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
        #endregion

        #region Methods
        /// <summary>
        /// Determines the interesting view properties.
        /// </summary>
        /// <returns>A list of names with view properties to subscribe to.</returns>
        private List<string> DetermineInterestingViewProperties()
        {
            var targetViewType = TargetViewType;

            return _viewPropertiesToSubscribe.GetFromCacheOrFetch(targetViewType, () =>
            {
                var viewProperties = TargetView.GetProperties();
                var finalProperties = new List<string>();

                if ((_viewPropertySelector == null) || (_viewPropertySelector.MustSubscribeToAllViewProperties(targetViewType)))
                {
                    finalProperties.AddRange(viewProperties);
                }
                else
                {
                    var propertiesToSubscribe = _viewPropertySelector.GetViewPropertiesToSubscribeTo(targetViewType);
                    if (!propertiesToSubscribe.Contains("DataContext"))
                    {
                        propertiesToSubscribe.Add("DataContext");
                    }

                    foreach (var gatheredViewProperty in viewProperties)
                    {
                        if (propertiesToSubscribe.Contains(gatheredViewProperty))
                        {
                            finalProperties.Add(gatheredViewProperty);
                        }
                    }
                }

                return finalProperties;
            });
        }

        /// <summary>
        /// Sets the data context of the target control.
        /// <para />
        /// This method is abstract because the real logic implementation knows how to set the data context (for example,
        /// by using an additional data context grid).
        /// </summary>
        /// <param name="newDataContext">The new data context.</param>
        protected abstract void SetDataContext(object newDataContext);

        /// <summary>
        /// Creates a view model by using data context or, if that is not possible, the constructor of the view model.
        /// </summary>
        protected IViewModel CreateViewModelByUsingDataContextOrConstructor()
        {
            var dataContext = TargetView.DataContext;

            // It might be possible that a view model is already set, so use it if the datacontext is a view model
            var dataContextAsIViewModel = dataContext as IViewModel;
            if ((dataContextAsIViewModel != null) && (dataContextAsIViewModel.GetType() == ViewModelType))
            {
                return dataContextAsIViewModel;
            }

            return ConstructViewModelUsingArgumentOrDefaultConstructor(dataContext);
        }

        /// <summary>
        /// Called when the <see cref="ViewModel"/> property is about to change.
        /// </summary>
        protected virtual void OnViewModelChanging()
        {
        }

        /// <summary>
        /// Called when the <see cref="ViewModel"/> property has just been changed.
        /// </summary>
        protected virtual void OnViewModelChanged()
        {
        }

        /// <summary>
        /// Called when a property on the view model has changed.
        /// </summary>
        /// <param name="viewModel">The view model.</param>
        /// <param name="e">The <see cref="System.ComponentModel.PropertyChangedEventArgs"/> instance containing the event data.</param>
        protected virtual void OnViewModelPropertyChanged(IViewModel viewModel, PropertyChangedEventArgs e)
        {
        }

        /// <summary>
        /// Called when the <see cref="TargetView"/> has just been loaded.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        /// <remarks>
        /// This method will call the <see cref="OnTargetViewLoaded"/> which can be overriden for custom 
        /// behavior. This method is required to protect from duplicate loaded events.
        /// </remarks>
        private void OnTargetViewLoadedInternal(object sender, EventArgs e)
        {
            // Don't do this again (another bug in WPF: OnLoaded is called more than OnUnloaded)
            if (IsTargetViewLoaded)
            {
                return;
            }

            if (!CanViewBeLoaded)
            {
                Log.Debug("Received Loaded or LayoutUpdated, but CanViewBeLoaded is false thus not treating the control as loaded");
                return;
            }

            Log.Debug("Target view '{0}' is loaded", TargetView.GetType().Name);

            InvokeViewLoadEvent(ViewLoadStateEvent.Loading);

            IsLoading = true;

            var view = TargetView;
            _viewManager.RegisterView(view);

            IsTargetViewLoaded = true;

            OnTargetViewLoaded(sender, e);

            TargetView.EnsureVisualTree();

            var targetViewAsViewModelContainer = TargetView as IViewModelContainer;
            if (targetViewAsViewModelContainer != null)
            {
                ViewToViewModelMappingHelper.InitializeViewToViewModelMappings(targetViewAsViewModelContainer);
            }

            TargetView.Dispatch(() =>
            {
                if (ViewModel != null)
                {
                    // Initialize the view model. The view model itself is responsible to prevent double initialization
                    ViewModel.InitializeViewModel();

                    // Revalidate since the control already initialized the view model before the control
                    // was visible, therefore the WPF engine does not show warnings and errors
                    var viewModelAsViewModelBase = ViewModel as ViewModelBase;
                    if (viewModelAsViewModelBase != null)
                    {
                        viewModelAsViewModelBase.Validate(true, false);
                    }
                    else
                    {
                        ViewModel.ValidateViewModel(true, false);
                    }

                    _isFirstValidationAfterLoaded = true;
                }
            });

            IsLoading = false;

            InvokeViewLoadEvent(ViewLoadStateEvent.Loaded);
        }

        /// <summary>
        /// Called when the <see cref="TargetView"/> has just been loaded.
        /// <para />
        /// The base implementation will try to create a view model based on the current DataContext and
        /// set it as the DataContext of the <see cref="TargetView"/>. To create custom logic for
        /// view model creation, override this method and do not call the base.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        public virtual void OnTargetViewLoaded(object sender, EventArgs e)
        {
            if (ViewModel == null)
            {
                ViewModel = CreateViewModelByUsingDataContextOrConstructor();
            }
        }

        /// <summary>
        /// Called when the <see cref="TargetView"/> has just been unloaded.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        /// <remarks>
        /// This method will call the <see cref="OnTargetViewUnloaded"/> which can be overriden for custom 
        /// behavior. This method is required to protect from duplicate unloaded events.
        /// </remarks>
        private void OnTargetViewUnloadedInternal(object sender, EventArgs e)
        {
            // Don't do this again (another bug in WPF: OnLoaded is called more than OnUnloaded)
            if (!IsTargetViewLoaded)
            {
                return;
            }

            InvokeViewLoadEvent(ViewLoadStateEvent.Unloading);

            IsUnloading = true;

            Log.Debug("Target control '{0}' is unloaded", TargetView.GetType().Name);

            var view = TargetView as IView;
            if (view == null)
            {
                Log.Warning("Cannot unregister view '{0}' in the view manager because it does not implement IView", TargetView.GetType().FullName);
            }
            else
            {
                _viewManager.UnregisterView(view);
            }

            IsTargetViewLoaded = false;
            _isFirstValidationAfterLoaded = true;

            OnTargetViewUnloaded(sender, e);

            var targetViewAsViewModelContainer = TargetView as IViewModelContainer;
            if (targetViewAsViewModelContainer != null)
            {
                ViewToViewModelMappingHelper.UninitializeViewToViewModelMappings(targetViewAsViewModelContainer);
            }

            IsUnloading = false;

            InvokeViewLoadEvent(ViewLoadStateEvent.Unloaded);
        }

        /// <summary>
        /// Called when the <see cref="TargetView"/> has just been unloaded.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        public virtual void OnTargetViewUnloaded(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Called when the <c>DataContext</c> property of the <see cref="TargetView" /> has changed.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        public virtual void OnTargetViewDataContextChanged(object sender, EventArgs e)
        {
            Log.Debug("DataContext of TargetView '{0}' has changed to '{1}'", TargetView.GetType().Name, ObjectToStringHelper.ToTypeString(TargetView.DataContext));

            var dataContext = TargetView.DataContext;
            if (dataContext == null)
            {
                return;
            }

            if (dataContext.IsSentinelBindingObject())
            {
                return;
            }

            if (ViewModel == dataContext)
            {
                return;
            }

            if (dataContext.GetType().IsAssignableFromEx(ViewModelType))
            {
                // Use the view model from the data context, probably set manually
                ViewModel = (IViewModel)dataContext;
            }
        }

        /// <summary>
        /// Called when a property on the <see cref="TargetView" /> has changed.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="PropertyChangedEventArgs"/> instance containing the event data.</param>
        private void OnTargetViewPropertyChangedInternal(object sender, PropertyChangedEventArgs e)
        {
            OnTargetViewPropertyChanged(sender, e);
        }

        /// <summary>
        /// Called when a property on the <see cref="TargetView" /> has changed.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="PropertyChangedEventArgs"/> instance containing the event data.</param>
        public virtual void OnTargetViewPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            TargetViewPropertyChanged.SafeInvoke(this, e);
        }

        /// <summary>
        /// Called when a property on the <see cref="ViewModel"/> has changed.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.ComponentModel.PropertyChangedEventArgs"/> instance containing the event data.</param>
        public virtual void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ViewModelPropertyChanged.SafeInvoke(this, e);
        }

        /// <summary>
        /// Called when the <see cref="ViewModel"/> has been saved.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        public virtual void OnViewModelCanceled(object sender, EventArgs e)
        {
            ViewModelCanceled.SafeInvoke(this, e);
        }

        /// <summary>
        /// Called when the <see cref="ViewModel"/> has been saved.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        public virtual void OnViewModelSaved(object sender, EventArgs e)
        {
            ViewModelSaved.SafeInvoke(this, e);
        }

        /// <summary>
        /// Called when the <see cref="ViewModel"/> has been closed.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="Catel.MVVM.ViewModelClosedEventArgs"/> instance containing the event data.</param>
        public virtual void OnViewModelClosed(object sender, ViewModelClosedEventArgs e)
        {
            ViewModelClosed.SafeInvoke(this, e);
        }

        /// <summary>
        /// Validates the view model.
        /// </summary>
        /// <returns><c>true</c> if the <see cref="ViewModel"/> is valid; otherwise <c>false</c>.</returns>
        public virtual bool ValidateViewModel()
        {
            if (ViewModel == null)
            {
                return false;
            }

            bool result = ViewModel.ValidateViewModel(_isFirstValidationAfterLoaded, false);

            _isFirstValidationAfterLoaded = false;

            return result;
        }

        /// <summary>
        /// Cancels the view model.
        /// </summary>
        /// <returns><c>true</c> if the view model is successfully canceled; otherwise <c>false</c>.</returns>
        public virtual bool CancelViewModel()
        {
            if (ViewModel == null)
            {
                return false;
            }

            return ViewModel.CancelViewModel();
        }

        /// <summary>
        /// Cancels and closes the view model.
        /// </summary>
        /// <returns><c>true</c> if the view model is successfully canceled; otherwise <c>false</c>.</returns>
        public bool CancelAndCloseViewModel()
        {
            bool result = CancelViewModel();
            if (!result)
            {
                return result;
            }

            CloseViewModel(result);

            return result;
        }

        /// <summary>
        /// Saves the view model.
        /// </summary>
        /// <returns><c>true</c> if the view model is successfully saved; otherwise <c>false</c>.</returns>
        public virtual bool SaveViewModel()
        {
            if (ViewModel == null)
            {
                return false;
            }

            return ViewModel.SaveViewModel();
        }

        /// <summary>
        /// Saves and closes the view model. If the saving fails, the view model is not closed.
        /// </summary>
        /// <returns><c>true</c> if the view model is successfully saved; otherwise <c>false</c>.</returns>
        public bool SaveAndCloseViewModel()
        {
            bool result = SaveViewModel();
            if (!result)
            {
                return result;
            }

            CloseViewModel(result);

            return result;
        }

        /// <summary>
        /// Closes the view model.
        /// </summary>
        public virtual void CloseViewModel(bool? result)
        {
            if (ViewModel != null)
            {
                ViewModel.CloseViewModel(result);
                ViewModel = null;
            }
        }

        /// <summary>
        /// Tries to construct the view model using the argument. If that fails, it will try to use
        /// the default constructor of the view model. If that is not available, <c>null</c> is returned.
        /// </summary>
        /// <param name="injectionObject">The object that is injected into the view model constructor.</param>
        /// <returns>
        /// Constructed view model or <c>null</c> if the view model could not be constructed.
        /// </returns>
        protected IViewModel ConstructViewModelUsingArgumentOrDefaultConstructor(object injectionObject)
        {
            return ConstructViewModelUsingArgumentOrDefaultConstructor(injectionObject, ViewModelType);
        }

        /// <summary>
        /// Tries to construct the view model using the argument. If that fails, it will try to use
        /// the default constructor of the view model. If that is not available, <c>null</c> is returned.
        /// </summary>
        /// <param name="injectionObject">The object that is injected into the view model constructor.</param>
        /// <param name="viewModelType">Type of the view model.</param>
        /// <returns>Constructed view model or <c>null</c> if the view model could not be constructed.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="viewModelType"/> is <c>null</c>.</exception>
        private IViewModel ConstructViewModelUsingArgumentOrDefaultConstructor(object injectionObject, Type viewModelType)
        {
            Argument.IsNotNull("viewModelType", viewModelType);

            if (ViewModelBehavior == LogicViewModelBehavior.Injected)
            {
                return ViewModel;
            }

            if (PreventViewModelCreation)
            {
                Log.Info("ViewModel construction is prevented by the PreventViewModelCreation property");
                return null;
            }

            if (IgnoreNullDataContext && (injectionObject == null))
            {
                Log.Info("ViewModel construction is prevented by the IgnoreNullDataContext property");
                return null;
            }

            var determineViewModelInstanceEventArgs = new DetermineViewModelInstanceEventArgs(injectionObject);
            DetermineViewModelInstance.SafeInvoke(this, determineViewModelInstanceEventArgs);
            if (determineViewModelInstanceEventArgs.ViewModel != null)
            {
                var viewModel = determineViewModelInstanceEventArgs.ViewModel;
                Log.Info("ViewModel instance is overriden by the DetermineViewModelInstance event, using view model of type '{0}'", viewModel.GetType().Name);

                return viewModel;
            }

            if (determineViewModelInstanceEventArgs.DoNotCreateViewModel)
            {
                Log.Info("ViewModel construction is prevented by the DetermineViewModelInstance event (DoNotCreateViewModel is set to true)");
                return null;
            }

            var determineViewModelTypeEventArgs = new DetermineViewModelTypeEventArgs(injectionObject);
            DetermineViewModelType.SafeInvoke(this, determineViewModelTypeEventArgs);
            if (determineViewModelTypeEventArgs.ViewModelType != null)
            {
                Log.Info("ViewModelType is overriden by the DetermineViewModelType event, using '{0}' instead of '{1}'",
                    determineViewModelTypeEventArgs.ViewModelType.FullName, viewModelType.FullName);

                viewModelType = determineViewModelTypeEventArgs.ViewModelType;
            }

            var injectionObjectAsViewModel = injectionObject as IViewModel;
            if (injectionObjectAsViewModel != null)
            {
                var injectionObjectViewModelType = injectionObject.GetType();

                if (ViewModelFactory.CanReuseViewModel(TargetViewType, viewModelType, injectionObjectViewModelType, injectionObject as IViewModel))
                {
                    Log.Info("DataContext of type '{0}' is allowed to be reused by view '{1}', using the current DataContext as view model",
                             viewModelType.FullName, TargetViewType.FullName);

                    return (IViewModel)injectionObject;
                }
            }

            Log.Debug("Using IViewModelFactory '{0}' to instantiate the view model", ViewModelFactory.GetType().FullName);

            var viewModelInstance = ViewModelFactory.CreateViewModel(viewModelType, injectionObject);

            Log.Debug("Used IViewModelFactory to instantiate view model, the factory did{0} return a valid view model",
                (viewModelInstance != null) ? string.Empty : " NOT");

            return viewModelInstance;
        }

        /// <summary>
        /// Invokes the specific view load event and makes sure that it isn't double invoked.
        /// </summary>
        /// <param name="viewLoadStateEvent">The view load state event.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">viewLoadStateEvent</exception>
        protected void InvokeViewLoadEvent(ViewLoadStateEvent viewLoadStateEvent)
        {
            if (_lastInvokedViewLoadStateEvent == viewLoadStateEvent)
            {
                return;
            }

            EventHandler<EventArgs> handler = null;

            switch (viewLoadStateEvent)
            {
                case ViewLoadStateEvent.Loading:
                    handler = ViewLoading;
                    break;

                case ViewLoadStateEvent.Loaded:
                    handler = ViewLoaded;
                    break;

                case ViewLoadStateEvent.Unloading:
                    handler = ViewUnloading;
                    break;

                case ViewLoadStateEvent.Unloaded:
                    handler = ViewUnloaded;
                    break;

                default:
                    throw new ArgumentOutOfRangeException("viewLoadStateEvent");
            }

            handler.SafeInvoke(this);

            _lastInvokedViewLoadStateEvent = viewLoadStateEvent;
        }
        #endregion
    }
}