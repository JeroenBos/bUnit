using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bunit
{
	/// <summary>
	/// Generalized Blazor renderer for testing purposes.
	/// </summary>
	public class TestRenderer : Renderer
	{
		private const string LOGGER_CATEGORY = nameof(Bunit) + "." + nameof(TestRenderer);
		private static readonly Type CascadingValueType = typeof(CascadingValue<>);
		private readonly RenderEventPublisher _renderEventPublisher;
		private readonly ILogger _logger;
		private Exception? _unhandledException;

		/// <inheritdoc/>
		public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

		/// <summary>
		/// Gets an <see cref="IObservable{RenderEvent}"/> which will provide subscribers with <see cref="RenderEvent"/>s from the
		/// <see cref="TestRenderer"/> during its life time.
		/// </summary>
		public IObservable<RenderEvent> RenderEvents { get; }

		/// <summary>
		/// Creates an instance of the <see cref="TestRenderer"/> class.
		/// </summary>
		public TestRenderer(IServiceProvider serviceProvider, ILoggerFactory loggerFactory) : base(serviceProvider, loggerFactory)
		{
			_renderEventPublisher = new RenderEventPublisher();
			_logger = loggerFactory?.CreateLogger(LOGGER_CATEGORY) ?? NullLogger.Instance;
			RenderEvents = _renderEventPublisher;
		}

		/// <summary>
		/// Instantiates and renders the component of type <typeparamref name="TComponent"/>.
		/// </summary>
		/// <typeparam name="TComponent">Type of component to render.</typeparam>
		/// <param name="parameters">Parameters to pass to the component during first render.</param>
		/// <returns>The component and its assigned id.</returns>
		public async Task<(int ComponentId, TComponent Component)> RenderComponent<TComponent>(params ComponentParameter[] parameters) where TComponent : IComponent
		{
			var componentType = typeof(TComponent);
			var renderFragment = CreateRenderFragment(componentType, parameters);
			var wrapperId = await RenderFragmentInsideWrapper(renderFragment).ConfigureAwait(false);
			return FindComponent<TComponent>(wrapperId);
		}

		public Task<int> RenderFragment(RenderFragment renderFragment)
		{
			return RenderFragmentInsideWrapper(renderFragment);
		}

		public (int ComponentId, TComponent Component) FindComponent<TComponent>(int parentComponentId)
		{
			var result = GetComponent<TComponent>(parentComponentId);
			if (result.HasValue)
				return result.Value;
			else
				throw new ComponentNotFoundException(typeof(TComponent));
		}

		public IReadOnlyList<(int ComponentId, TComponent Component)> FindComponents<TComponent>(int parentComponentId)
		{
			return GetComponents<TComponent>(parentComponentId);
		}

		/// <inheritdoc/>
		public new ArrayRange<RenderTreeFrame> GetCurrentRenderTreeFrames(int componentId)
		{
			try
			{
				return base.GetCurrentRenderTreeFrames(componentId);
			}
			catch (ArgumentException ex) when (ex.Message.Equals($"The renderer does not have a component with ID {componentId}.", StringComparison.Ordinal))
			{
				_logger.LogDebug(new EventId(2, nameof(GetCurrentRenderTreeFrames)), $"{ex.Message}");
			}
			return new ArrayRange<RenderTreeFrame>(Array.Empty<RenderTreeFrame>(), 0);
		}

		/// <inheritdoc/>
		public new Task DispatchEventAsync(ulong eventHandlerId, EventFieldInfo fieldInfo, EventArgs eventArgs)
		{
			if (fieldInfo is null)
				throw new ArgumentNullException(nameof(fieldInfo));
			_logger.LogDebug(new EventId(1, nameof(DispatchEventAsync)), $"Starting trigger of '{fieldInfo.FieldValue}'");

			var task = Dispatcher.InvokeAsync(() =>
			{
				try
				{
					return base.DispatchEventAsync(eventHandlerId, fieldInfo, eventArgs);
				}
				catch (Exception e)
				{
					_unhandledException = e;
					throw;
				}
			});

			AssertNoUnhandledExceptions();

			_logger.LogDebug(new EventId(1, nameof(DispatchEventAsync)), $"Finished trigger of '{fieldInfo.FieldValue}'");
			return task;
		}

		/// <summary>
		/// Dispatches an callback in the context of the renderer synchronously and 
		/// asserts no errors happened during dispatch
		/// </summary>
		/// <param name="callback"></param>
		public void InvokeAsync(Action callback)
		{
			Dispatcher.InvokeAsync(callback).Wait();
			AssertNoUnhandledExceptions();
		}

		private async Task<int> RenderFragmentInsideWrapper(RenderFragment renderFragment)
		{
			var wrapper = new WrapperComponent();
			var wrapperId = AssignRootComponentId(wrapper);

			await Dispatcher.InvokeAsync(() => wrapper.Render(renderFragment)).ConfigureAwait(false);

			AssertNoUnhandledExceptions();

			return wrapperId;
		}

		/// <inheritdoc/>
		protected override void HandleException(Exception exception)
			=> _unhandledException = exception;

		/// <inheritdoc/>
		protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
		{
			_logger.LogDebug(new EventId(0, nameof(UpdateDisplayAsync)), $"New render batch with ReferenceFrames = {renderBatch.ReferenceFrames.Count}, UpdatedComponents = {renderBatch.UpdatedComponents.Count}, DisposedComponentIDs = {renderBatch.DisposedComponentIDs.Count}, DisposedEventHandlerIDs = {renderBatch.DisposedEventHandlerIDs.Count}");
			var renderEvent = new RenderEvent(in renderBatch, this);
			_renderEventPublisher.OnRender(renderEvent);
			return Task.CompletedTask;
		}

		/// <inheritdoc/>
		protected override void Dispose(bool disposing)
		{
			_renderEventPublisher.OnCompleted();
			base.Dispose(disposing);
		}

		private void AssertNoUnhandledExceptions()
		{
			if (_unhandledException is { } unhandled)
			{
				_logger.LogError(new EventId(3, nameof(AssertNoUnhandledExceptions)), $"An unhandled exception happened during rendering: {unhandled.Message}");
				_unhandledException = null;
				ExceptionDispatchInfo.Capture(unhandled).Throw();
			}
		}

		private (int ComponentId, TComponent Component)? GetComponent<TComponent>(int rootComponentId)
		{
			var ownFrames = GetCurrentRenderTreeFrames(rootComponentId);

			for (int i = 0; i < ownFrames.Count; i++)
			{
				ref var frame = ref ownFrames.Array[i];
				if (frame.FrameType == RenderTreeFrameType.Component)
				{
					if (frame.Component is TComponent component)
					{
						return (frame.ComponentId, component);
					}
					var result = GetComponent<TComponent>(frame.ComponentId);
					if (result != null)
						return result;
				}
			}

			throw new ComponentNotFoundException(typeof(TComponent));
		}

		private IReadOnlyList<(int ComponentId, TComponent Component)> GetComponents<TComponent>(int rootComponentId)
		{
			var ownFrames = GetCurrentRenderTreeFrames(rootComponentId);

			if (ownFrames.Count == 0)
				return Array.Empty<(int Id, TComponent Component)>();

			var result = new List<(int ComponentId, TComponent Component)>();

			for (int i = 0; i < ownFrames.Count; i++)
			{
				ref var frame = ref ownFrames.Array[i];
				if (frame.FrameType == RenderTreeFrameType.Component)
				{
					if (frame.Component is TComponent component)
					{
						result.Add((frame.ComponentId, component));
					}
					result.AddRange(GetComponents<TComponent>(frame.ComponentId));
				}
			}
			return result;
		}

		private static RenderFragment CreateRenderFragment(Type componentType, IReadOnlyList<ComponentParameter> parameters)
		{
			var cascadingParams = new Queue<ComponentParameter>(parameters.Where(x => x.IsCascadingValue));

			if (cascadingParams.Count > 0)
				return CreateCascadingValueRenderFragment(componentType, cascadingParams, parameters);
			else
				return CreateComponentRenderFragment(componentType, parameters);

			static RenderFragment CreateCascadingValueRenderFragment(Type componentType, Queue<ComponentParameter> cascadingParams, IReadOnlyList<ComponentParameter> parameters)
			{
				var cp = cascadingParams.Dequeue();
				var cascadingValueType = CreateCascadingValueType(cp);
				return builder =>
				{
					builder.OpenComponent(0, cascadingValueType);
					if (cp.Name is { })
						builder.AddAttribute(1, nameof(CascadingValue<object>.Name), cp.Name);

					builder.AddAttribute(2, nameof(CascadingValue<object>.Value), cp.Value);
					builder.AddAttribute(3, nameof(CascadingValue<object>.IsFixed), true);

					if (cascadingParams.Count > 0)
						builder.AddAttribute(4, nameof(CascadingValue<object>.ChildContent), CreateCascadingValueRenderFragment(componentType, cascadingParams, parameters));
					else
						builder.AddAttribute(4, nameof(CascadingValue<object>.ChildContent), CreateComponentRenderFragment(componentType, parameters));

					builder.CloseComponent();
				};
			}

			static RenderFragment CreateComponentRenderFragment(Type componentType, IReadOnlyList<ComponentParameter> parameters)
			{
				return builder =>
				{
					builder.OpenComponent(0, componentType);

					for (int i = 0; i < parameters.Count; i++)
					{
						var para = parameters[i];
						if (!para.IsCascadingValue)
							builder.AddAttribute(i + 1, para.Name, para.Value);
					}

					builder.CloseComponent();
				};
			}
		}

		private static Type CreateCascadingValueType(ComponentParameter parameter)
		{
			if (parameter.Value is null)
				throw new InvalidOperationException("Cannot get the type of a null object");
			var cascadingValueType = parameter.Value.GetType();
			return CascadingValueType.MakeGenericType(cascadingValueType);
		}

		/// <summary>
		/// Wrapper class that provides access to a <see cref="RenderHandle"/>.
		/// </summary>
		private class WrapperComponent : IComponent
		{
			private RenderHandle _renderHandle;

			public void Attach(RenderHandle renderHandle) => _renderHandle = renderHandle;

			public Task SetParametersAsync(ParameterView parameters) => throw new InvalidOperationException($"WrapperComponent shouldn't receive any parameters");

			public void Render(RenderFragment renderFragment)
			{
				_renderHandle.Render(renderFragment);
			}
		}
	}
}
