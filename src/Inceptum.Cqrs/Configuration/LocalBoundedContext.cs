﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using EventStore.ClientAPI;
using Inceptum.Cqrs.InfrastructureCommands;
using Inceptum.Cqrs.Routing;
using Inceptum.Messaging.Configuration;
using NEventStore;
using NEventStore.Dispatcher;

namespace Inceptum.Cqrs.Configuration
{
    public interface IHideObjectMembers
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        string ToString();

        [EditorBrowsable(EditorBrowsableState.Never)]
        Type GetType();

        [EditorBrowsable(EditorBrowsableState.Never)]
        int GetHashCode();

        [EditorBrowsable(EditorBrowsableState.Never)]
        bool Equals(object obj);
    }
 


    public static class LocalBoundedContext
    {
 

        public static BoundedContextRegistration1 Named(string name)
        {
            return new BoundedContextRegistration1(name);
        }
    }


    public static class BoundedContextRegistrationExtensions
    {
        public static PublishingCommandsDescriptor WithLoopback(this ListeningCommandsDescriptor descriptor, string route=null)
        {
            route = route ?? descriptor.Route;
            return descriptor.PublishingCommands(descriptor.Types).To(descriptor.BoundedContextName).With(route);
        }

        public static ListeningEventsDescriptor WithLoopback(this PublishingEventsDescriptor descriptor, string route=null)
        {
            route = route ?? descriptor.Route;
            return descriptor.ListeningEvents(descriptor.Types).From(descriptor.BoundedContextName).On(route);
        }

        public static  IListeningRouteDescriptor<ListeningCommandsDescriptor> ListeningInfrastructureCommands(this IBoundedContextRegistration registration)
        {
            return registration.ListeningCommands(typeof(ReplayEventsCommand));
        }

        public static PublishingCommandsDescriptor PublishingInfrastructureCommands(this IBoundedContextRegistration registration)
        {
            return registration.PublishingCommands(typeof(ReplayEventsCommand));
        }
    }

    public interface IBoundedContextRegistration : IRegistration, IHideObjectMembers
    {
        string BoundedContextName { get; }
    

        IBoundedContextRegistration FailedCommandRetryDelay(long delay);
        PublishingCommandsDescriptor PublishingCommands(params Type[] commandsTypes);
        ListeningEventsDescriptor ListeningEvents(params Type[] type);
        IListeningRouteDescriptor<ListeningCommandsDescriptor> ListeningCommands(params Type[] type);
        IPublishingRouteDescriptor<PublishingEventsDescriptor> PublishingEvents(params Type[] type);
        ProcessingOptionsDescriptor ProcessingOptions(string route);

        IBoundedContextRegistration WithProjection(object projection, string fromBoundContext);
        IBoundedContextRegistration WithProjection(Type projection, string fromBoundContext);
        IBoundedContextRegistration WithProjection<TListener>(string fromBoundContext);
        IBoundedContextRegistration WithCommandsHandler(object handler);
        IBoundedContextRegistration WithCommandsHandler<T>();
        IBoundedContextRegistration WithCommandsHandlers(params Type[] handlers);
        IBoundedContextRegistration WithCommandsHandler(Type handler);

        IBoundedContextRegistration WithEventStore(Func<IDispatchCommits, Wireup> configureEventStore);
        IBoundedContextRegistration WithEventStore(IEventStoreConnection eventStoreConnection);


        IBoundedContextRegistration WithProcess(object process);
        IBoundedContextRegistration WithProcess(Type process);
        IBoundedContextRegistration WithProcess<TProcess>() where TProcess : IProcess;
    }

    public class BoundedContextRegistration1 : IBoundedContextRegistration
    {
        public string BoundedContextName { get; private set; }
        private readonly List<IBoundedContextDescriptor> m_Descriptors = new List<IBoundedContextDescriptor>();
        private Type[] m_Dependencies=new Type[0];

        public BoundedContextRegistration1(string boundedContextName)
        {
            BoundedContextName = boundedContextName;
            AddDescriptor(new InfrastructureCommandsHandlerDescriptor());
        }

        public long FailedCommandRetryDelayInternal { get; set; }

        protected T AddDescriptor<T>(T descriptor) where T : IBoundedContextDescriptor
        {
            m_Dependencies = m_Dependencies.Concat(descriptor.GetDependencies()).Distinct().ToArray();
            m_Descriptors.Add(descriptor);
            return descriptor;
        }

        public PublishingCommandsDescriptor PublishingCommands(params Type[] commandsTypes)
        {
            return AddDescriptor(new PublishingCommandsDescriptor(this, commandsTypes));
        }

        public ListeningEventsDescriptor ListeningEvents(params Type[] types)
        {
            return AddDescriptor(new ListeningEventsDescriptor(this, types));
        }

        public IListeningRouteDescriptor<ListeningCommandsDescriptor> ListeningCommands(params Type[] types)
        {
            return AddDescriptor(new ListeningCommandsDescriptor(this, types));
        }

        public IPublishingRouteDescriptor<PublishingEventsDescriptor> PublishingEvents(params Type[] types)
        {
            return AddDescriptor(new PublishingEventsDescriptor(this, types));
        }

        public ProcessingOptionsDescriptor ProcessingOptions(string route)
        {
            return AddDescriptor(new ProcessingOptionsDescriptor(this, route));
        }


        public IBoundedContextRegistration WithEventStore(Func<IDispatchCommits, Wireup> configureEventStore)
        {
            AddDescriptor(new EventStoreDescriptor(configureEventStore));
            return this;
        }
        public IBoundedContextRegistration WithEventStore(IEventStoreConnection eventStoreConnection)
        {
            AddDescriptor(new GetEventStoreDescriptor(eventStoreConnection));
            return this;
        }

        public IBoundedContextRegistration FailedCommandRetryDelay(long delay)
        {
            if (delay < 0) throw new ArgumentException("threadCount should be greater or equal to 0", "delay");
            FailedCommandRetryDelayInternal = delay;
            return this;
        }


        void IRegistration.Create(CqrsEngine cqrsEngine)
        {
            var boundedContext = new BoundedContext(cqrsEngine, BoundedContextName, 1, FailedCommandRetryDelayInternal, true, BoundedContextName);
            foreach (var descriptor in m_Descriptors)
            {
                descriptor.Create(boundedContext, cqrsEngine.DependencyResolver);
            }
            cqrsEngine.BoundedContexts.Add(boundedContext);

        }

        void IRegistration.Process(CqrsEngine cqrsEngine)
        {
            var boundedContext = cqrsEngine.BoundedContexts.FirstOrDefault(bc => bc.Name == BoundedContextName);
            foreach (var descriptor in m_Descriptors)
            {
                descriptor.Process(boundedContext, cqrsEngine);
            }
        }

        public IBoundedContextRegistration WithCommandsHandler(object handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            AddDescriptor(new CommandsHandlerDescriptor(handler));
            return this;
        }
        public IBoundedContextRegistration WithCommandsHandler<T>()
        {
            AddDescriptor(new CommandsHandlerDescriptor(typeof(T)));
            return this;
        }

        public IBoundedContextRegistration WithCommandsHandlers(params Type[] handlers)
        {
            AddDescriptor(new CommandsHandlerDescriptor(handlers));
            return this;
        }

        public IBoundedContextRegistration WithCommandsHandler(Type handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            AddDescriptor(new CommandsHandlerDescriptor(handler));
            return this;
        }


        public IBoundedContextRegistration WithProjection(object projection, string fromBoundContext)
        {
            RegisterProjections(projection, fromBoundContext);
            return this;
        }

        public IBoundedContextRegistration WithProjection(Type projection, string fromBoundContext)
        {
            RegisterProjections(projection, fromBoundContext);
            return this;
        }

        public IBoundedContextRegistration WithProjection<TListener>(string fromBoundContext)
        {
            RegisterProjections(typeof(TListener), fromBoundContext);
            return this;
        }

        protected void RegisterProjections(object projection, string fromBoundContext)
        {
            if (projection == null) throw new ArgumentNullException("projection");
            AddDescriptor(new ProjectionDescriptor(projection, fromBoundContext));
        }

        protected void RegisterProjections(Type projection, string fromBoundContext)
        {
            if (projection == null) throw new ArgumentNullException("projection");
            AddDescriptor(new ProjectionDescriptor(projection, fromBoundContext));
        }

        public IBoundedContextRegistration WithProcess(object process)
        {
            AddDescriptor(new LocalProcessDescriptor(process));
            return this;
        }

        public IBoundedContextRegistration WithProcess(Type process)
        {
            AddDescriptor(new LocalProcessDescriptor(process));
            return this;
        }

        public IBoundedContextRegistration WithProcess<TProcess>()
            where TProcess : IProcess
        {
            return WithProcess(typeof(TProcess));
        }

        IEnumerable<Type> IRegistration.Dependencies
        {
            get { return m_Dependencies; }
        }
    }

    public class ProcessingOptionsDescriptor : BoundedContextRegistrationWrapper, IBoundedContextDescriptor
    {
        private readonly string m_Route;
        private uint m_ThreadCount;

        public ProcessingOptionsDescriptor(BoundedContextRegistration1 registration, string route) : base(registration)
        {
            m_Route = route;
        }

        public IEnumerable<Type> GetDependencies()
        {
            return new Type[0];
        }

        public void Create(BoundedContext boundedContext, IDependencyResolver resolver)
        {
            boundedContext.Routes[m_Route].ConcurrencyLevel = m_ThreadCount;
        }

        public void Process(BoundedContext boundedContext, CqrsEngine cqrsEngine)
        {
        }

        public ProcessingOptionsDescriptor MultiThreaded(uint threadCount)
        {
            if (threadCount == 0)
                throw new ArgumentOutOfRangeException("threadCount", "threadCount should be greater then 0");
            m_ThreadCount = threadCount;
            return this;
        }
    }

    public class PublishingEventsDescriptor : PublishingRouteDescriptor<PublishingEventsDescriptor>
    {
        public Type[] Types { get; private set; }

        public PublishingEventsDescriptor(BoundedContextRegistration1 registration, Type[] types) : base(registration)
        {
            Descriptor = this;
            Types = types;
        }

        public override IEnumerable<Type> GetDependencies()
        {
            return new Type[0];
        }

        public override void Create(BoundedContext boundedContext, IDependencyResolver resolver)
        {
           
        }

        public override void Process(BoundedContext boundedContext, CqrsEngine cqrsEngine)
        {
            foreach (var eventType in Types)
            {
                boundedContext.Routes[Route].AddPublishedEvent(eventType, 0, cqrsEngine.EndpointResolver);
            }
        }
    }

    public class PublishingCommandsDescriptor : PublishingRouteDescriptor<PublishingCommandsDescriptor>
    {
        private string m_BoundedContext;
        private readonly Type[] m_CommandsTypes;

        public PublishingCommandsDescriptor(BoundedContextRegistration1 registration, Type[] commandsTypes):base(registration)
        {
            m_CommandsTypes = commandsTypes;
            Descriptor = this;
        }

        public override IEnumerable<Type> GetDependencies()
        {
            return new Type[0];
        }

        public override void Create(BoundedContext boundedContext, IDependencyResolver resolver)
        {
             
        }

        public override void Process(BoundedContext boundedContext, CqrsEngine cqrsEngine)
        {            foreach (var type in m_CommandsTypes)
            {
                boundedContext.Routes[Route].AddPublishedCommand(type, 0, m_BoundedContext,cqrsEngine.EndpointResolver);
            }

        }

        public IPublishingRouteDescriptor<PublishingCommandsDescriptor> To(string boundedContext)
        {
            m_BoundedContext = boundedContext;
            return this;
        }

       
    }

    public class ListeningCommandsDescriptor : ListeningRouteDescriptor<ListeningCommandsDescriptor>
    {
        public Type[] Types { get; private set; }

        public ListeningCommandsDescriptor(BoundedContextRegistration1 registration, Type[] types) : base(registration)
        {
            Types = types;
            Descriptor = this;
        }


        public override IEnumerable<Type> GetDependencies()
        {
            return new Type[0];
        }

        public override void Create(BoundedContext boundedContext, IDependencyResolver resolver)
        {
        }

        public override void Process(BoundedContext boundedContext, CqrsEngine cqrsEngine)
        {
            foreach (var type in Types)
            {
                for (uint priority = 0; priority <= LowestPriority; priority++)
                {
                    var endpointResolver = new MapEndpointResolver(ExplicitEndpointSelectors, cqrsEngine.EndpointResolver);
                    boundedContext.Routes[Route].AddSubscribedCommand(type, priority, endpointResolver);
                }
            }
        }
 

    }

    public class ListeningEventsDescriptor : ListeningRouteDescriptor<ListeningEventsDescriptor>
    {
        private string m_BoundedContext;
        private readonly Type[] m_Types;

        public ListeningEventsDescriptor(BoundedContextRegistration1 registration, Type[] types) : base(registration)
        {
            m_Types = types;
            Descriptor = this;
        }

        public IListeningRouteDescriptor<ListeningEventsDescriptor> From(string boundedContext)
        {
            m_BoundedContext = boundedContext;
            return this;
        }

        public override IEnumerable<Type> GetDependencies()
        {
            return new Type[0];
        }

        public override void Create(BoundedContext boundedContext, IDependencyResolver resolver)
        {
        }

        public override void Process(BoundedContext boundedContext, CqrsEngine cqrsEngine)
        {
            foreach (var type in m_Types)
            {
                boundedContext.Routes[Route].AddSubscribedEvent(type, 0,m_BoundedContext,cqrsEngine.EndpointResolver);
            }
        }
    }


    public class ExplicitEndpointDescriptor<T> where T : RouteDescriptorBase
    {
        private readonly string m_Endpoint;
        private readonly T m_Descriptor;

        public ExplicitEndpointDescriptor(string endpoint, T descriptor)
        {
            m_Descriptor = descriptor;
            m_Endpoint = endpoint;
        }

        public T For(Func<RoutingKey, bool> criteria)
        {
            m_Descriptor.AddExplicitEndpoint(criteria, m_Endpoint);
            return m_Descriptor;
        }
    }


    public interface IListeningRouteDescriptor<out T> : IBoundedContextDescriptor
    {
        T On(string route);
    }

    public abstract class ListeningRouteDescriptor<T> : RouteDescriptorBase<T>, IListeningRouteDescriptor<T> where T :  RouteDescriptorBase
    {
        protected T Descriptor { private get; set; }

        protected ListeningRouteDescriptor(BoundedContextRegistration1 registration) : base(registration)
        {
        }

        protected internal string Route { get; private set; }

        T IListeningRouteDescriptor<T>.On(string route)
        {
            Route = route;
            return Descriptor;
        }

        public abstract IEnumerable<Type> GetDependencies();
        public abstract void Create(BoundedContext boundedContext, IDependencyResolver resolver);
        public abstract void Process(BoundedContext boundedContext, CqrsEngine cqrsEngine);

    }

    public interface IPublishingRouteDescriptor<out T> : IBoundedContextDescriptor 
    {
        T  With(string route);
    }

    public abstract class PublishingRouteDescriptor<T> : RouteDescriptorBase<T>, IPublishingRouteDescriptor<T> where T : RouteDescriptorBase
    {
        protected T Descriptor { private get; set; }
        protected internal  string Route { get; private set; }

        protected PublishingRouteDescriptor(BoundedContextRegistration1 registration):base(registration)
        {
        }

        T IPublishingRouteDescriptor<T>.With(string route)
        {
            Route = route;
            return Descriptor;
        }

        public abstract IEnumerable<Type> GetDependencies();
        public abstract void Create(BoundedContext boundedContext, IDependencyResolver resolver);
        public abstract void Process(BoundedContext boundedContext, CqrsEngine cqrsEngine);

    }

    public abstract class RouteDescriptorBase<T> : RouteDescriptorBase where T :  RouteDescriptorBase
    {

        protected RouteDescriptorBase(BoundedContextRegistration1 registration) : base(registration)
        {
        }

        public T Prioritized(uint lowestPriority)
        {
            LowestPriority = lowestPriority;
            return this as T;
        }


        public ExplicitEndpointDescriptor<T> WithEndpoint(string endpoint)
        {
            return new ExplicitEndpointDescriptor<T>(endpoint, this as T);
        }
    }


    public abstract class RouteDescriptorBase : BoundedContextRegistrationWrapper
    {
        private readonly Dictionary<Func<RoutingKey, bool>, string> m_ExplicitEndpointSelectors = new Dictionary<Func<RoutingKey, bool>, string>();

        protected Dictionary<Func<RoutingKey, bool>, string> ExplicitEndpointSelectors
        {
            get { return m_ExplicitEndpointSelectors; }
        }

        protected uint LowestPriority { get; set; }

        protected RouteDescriptorBase(BoundedContextRegistration1 registration) : base(registration)
        {
        }

        internal void AddExplicitEndpoint(Func<RoutingKey, bool> criteria, string endpoint)
        {
            ExplicitEndpointSelectors.Add(criteria, endpoint);
        }
    }

    public abstract class BoundedContextRegistrationWrapper : IBoundedContextRegistration
    {
        private readonly BoundedContextRegistration1 m_Registration;
        public long FailedCommandRetryDelayInternal
        {
            get { return m_Registration.FailedCommandRetryDelayInternal; }
            set { m_Registration.FailedCommandRetryDelayInternal = value; }
        }

        public IBoundedContextRegistration FailedCommandRetryDelay(long delay)
        {
            return m_Registration.FailedCommandRetryDelay(delay);
        }

        protected BoundedContextRegistrationWrapper(BoundedContextRegistration1 registration)
        {
            m_Registration = registration;
        }
        public string BoundedContextName
        {
            get { return m_Registration.BoundedContextName; }
        }

        public PublishingCommandsDescriptor PublishingCommands(params Type[] commandsTypes)
        {
            return m_Registration.PublishingCommands(commandsTypes);
        }

        public ListeningEventsDescriptor ListeningEvents(params Type[] types)
        {
            return m_Registration.ListeningEvents(types);
        }

        public IListeningRouteDescriptor<ListeningCommandsDescriptor> ListeningCommands(params Type[] types)
        {
            return m_Registration.ListeningCommands(types);
        }

        public IPublishingRouteDescriptor<PublishingEventsDescriptor> PublishingEvents(params Type[] types)
        {
            return m_Registration.PublishingEvents(types);
        }

        public ProcessingOptionsDescriptor ProcessingOptions(string route)
        {
            return m_Registration.ProcessingOptions(route);
        }

        void IRegistration.Create(CqrsEngine cqrsEngine)
        {
            (m_Registration as IRegistration).Create(cqrsEngine);
        }

        void IRegistration.Process(CqrsEngine cqrsEngine)
        {
            (m_Registration as IRegistration).Process(cqrsEngine);
        }

        public IBoundedContextRegistration WithCommandsHandler(object handler)
        {
            return m_Registration.WithCommandsHandler(handler);
        }

        public IBoundedContextRegistration WithCommandsHandler<T>()
        {
            return m_Registration.WithCommandsHandler<T>();
        }

        public IBoundedContextRegistration WithCommandsHandlers(params Type[] handlers)
        {
            return m_Registration.WithCommandsHandlers(handlers);
        }

        public IBoundedContextRegistration WithCommandsHandler(Type handler)
        {
            return m_Registration.WithCommandsHandler(handler);
        }

        IEnumerable<Type> IRegistration.Dependencies
        {
            get { return (m_Registration as IRegistration).Dependencies; }
        }


        public IBoundedContextRegistration WithEventStore(Func<IDispatchCommits, Wireup> configureEventStore)
        {
            return m_Registration.WithEventStore(configureEventStore);
        }

        public IBoundedContextRegistration WithEventStore(IEventStoreConnection eventStoreConnection)
        {
            return m_Registration.WithEventStore(eventStoreConnection);
        }

        public IBoundedContextRegistration WithProjection(object projection, string fromBoundContext)
        {
            return m_Registration.WithProjection(projection, fromBoundContext);
        }

        public IBoundedContextRegistration WithProjection(Type projection, string fromBoundContext)
        {
            return m_Registration.WithProjection(projection, fromBoundContext);
        }

        public IBoundedContextRegistration WithProjection<TListener>(string fromBoundContext)
        {
            return m_Registration.WithProjection<TListener>(fromBoundContext);
        }

        public IBoundedContextRegistration WithProcess(object process)
        {
            return m_Registration.WithProcess(process);
        }

        public IBoundedContextRegistration WithProcess(Type process)
        {
            return m_Registration.WithProcess(process);
        }

        public IBoundedContextRegistration WithProcess<TProcess>() where TProcess : IProcess
        {
            return m_Registration.WithProcess<TProcess>();
        }
    }
}