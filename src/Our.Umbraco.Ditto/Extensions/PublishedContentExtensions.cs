﻿namespace Our.Umbraco.Ditto
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Web;

    using global::Umbraco.Core;
    using global::Umbraco.Core.Models;
    using global::Umbraco.Web;

    /// <summary>
    /// Encapsulates extension methods for <see cref="IPublishedContent"/>.
    /// </summary>
    public static class PublishedContentExtensions
    {
        /// <summary>
        /// The cache for storing constructor parameter information.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, ParameterInfo[]> ConstructorCache
            = new ConcurrentDictionary<Type, ParameterInfo[]>();

        /// <summary>
        /// The cache for storing type property information.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> VirtualPropertyCache
            = new ConcurrentDictionary<Type, PropertyInfo[]>();

        /// <summary>
        /// The cache for storing type property information.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache
            = new ConcurrentDictionary<Type, PropertyInfo[]>();

        /// <summary>
        /// Returns the given instance of <see cref="IPublishedContent"/> as the specified type.
        /// </summary>
        /// <param name="content">
        /// The <see cref="IPublishedContent"/> to convert.
        /// </param>
        /// <param name="convertingType">
        /// The <see cref="Action{ConvertingTypeEventArgs}"/> to fire when converting.
        /// </param>
        /// <param name="convertedType">
        /// The <see cref="Action{ConvertedTypeEventArgs}"/> to fire when converted.
        /// </param>
        /// <param name="culture">
        /// The <see cref="CultureInfo"/>
        /// </param>
        /// <typeparam name="T">
        /// The <see cref="Type"/> of items to return.
        /// </typeparam>
        /// <returns>
        /// The resolved <see cref="T"/>.
        /// </returns>
        public static T As<T>(
            this IPublishedContent content,
            Action<ConvertingTypeEventArgs> convertingType = null,
            Action<ConvertedTypeEventArgs> convertedType = null,
            CultureInfo culture = null)
            where T : class
        {
            return content.As(typeof(T), convertingType, convertedType, culture) as T;
        }

        /// <summary>
        /// Gets a collection of the given type from the given <see cref="IEnumerable{IPublishedContent}"/>.
        /// </summary>
        /// <param name="items">
        /// The <see cref="IEnumerable{IPublishedContent}"/> to convert.
        /// </param>
        /// <param name="documentTypeAlias">
        /// The document type alias.
        /// </param>
        /// <param name="convertingType">
        /// The <see cref="Action{ConvertingTypeEventArgs}"/> to fire when converting.
        /// </param>
        /// <param name="convertedType">
        /// The <see cref="Action{ConvertedTypeEventArgs}"/> to fire when converted.
        /// </param>
        /// <param name="culture">The <see cref="CultureInfo"/></param>
        /// <typeparam name="T">
        /// The <see cref="Type"/> of items to return.
        /// </typeparam>
        /// <returns>
        /// The resolved <see cref="IEnumerable{T}"/>.
        /// </returns>
        public static IEnumerable<T> As<T>(
            this IEnumerable<IPublishedContent> items,
            string documentTypeAlias = null,
            Action<ConvertingTypeEventArgs> convertingType = null,
            Action<ConvertedTypeEventArgs> convertedType = null,
            CultureInfo culture = null)
            where T : class
        {
            return items.As(typeof(T), documentTypeAlias, convertingType, convertedType, culture)
                        .Select(x => x as T);
        }

        /// <summary>
        /// Gets a collection of the given type from the given <see cref="IEnumerable{IPublishedContent}"/>.
        /// </summary>
        /// <param name="items">
        /// The <see cref="IEnumerable{IPublishedContent}"/> to convert.
        /// </param>
        /// <param name="type">
        /// The <see cref="Type"/> of items to return.
        /// </param>
        /// <param name="documentTypeAlias">
        /// The document type alias.
        /// </param>
        /// <param name="convertingType">
        /// The <see cref="Action{ConvertingTypeEventArgs}"/> to fire when converting.
        /// </param>
        /// <param name="convertedType">
        /// The <see cref="Action{ConvertedTypeEventArgs}"/> to fire when converted.
        /// </param>
        /// <param name="culture">
        /// The <see cref="CultureInfo"/>.
        /// </param>
        /// <returns>
        /// The resolved <see cref="IEnumerable{T}"/>.
        /// </returns>
        public static IEnumerable<object> As(
            this IEnumerable<IPublishedContent> items,
            Type type,
            string documentTypeAlias = null,
            Action<ConvertingTypeEventArgs> convertingType = null,
            Action<ConvertedTypeEventArgs> convertedType = null,
            CultureInfo culture = null)
        {
            using (DisposableTimer.DebugDuration<IEnumerable<object>>(string.Format("IEnumerable As ({0})", documentTypeAlias)))
            {
                IEnumerable<object> typedItems;
                if (string.IsNullOrWhiteSpace(documentTypeAlias))
                {
                    typedItems = items.Select(x => x.As(type, convertingType, convertedType, culture));
                }
                else
                {
                    typedItems = items.Where(x => documentTypeAlias.InvariantEquals(x.DocumentTypeAlias))
                                      .Select(x => x.As(type, convertingType, convertedType, culture));
                }

                // We need to cast back here as nothing is strong typed anymore.
                return (IEnumerable<object>)EnumerableInvocations.Cast(type, typedItems);
            }
        }

        /// <summary>
        /// Returns an object representing the given <see cref="Type"/>.
        /// </summary>
        /// <param name="content">
        /// The <see cref="IPublishedContent"/> to convert.
        /// </param>
        /// <param name="type">
        /// The <see cref="Type"/> of items to return.
        /// </param>
        /// <param name="convertingType">
        /// The <see cref="Action{ConvertingTypeEventArgs}"/> to fire when converting.
        /// </param>
        /// <param name="convertedType">
        /// The <see cref="Action{ConvertedTypeEventArgs}"/> to fire when converted.
        /// </param>
        /// <param name="culture">The <see cref="CultureInfo"/></param>
        /// <returns>
        /// The converted <see cref="Object"/> as the given type.
        /// </returns>
        public static object As(
            this IPublishedContent content,
            Type type,
            Action<ConvertingTypeEventArgs> convertingType = null,
            Action<ConvertedTypeEventArgs> convertedType = null,
            CultureInfo culture = null)
        {
            if (content == null)
            {
                return null;
            }

            using (DisposableTimer.DebugDuration<object>(string.Format("IPublishedContent As ({0})", content.DocumentTypeAlias), "Complete"))
            {
                // Check for and fire any event args
                var convertingArgs = new ConvertingTypeEventArgs
                {
                    Content = content
                };

                EventHandlers.CallConvertingTypeHandler(convertingArgs);

                if (!convertingArgs.Cancel && convertingType != null)
                {
                    convertingType(convertingArgs);
                }

                // Cancel if applicable. 
                if (convertingArgs.Cancel)
                {
                    return null;
                }

                // Create an object and fetch it as the type.
                object instance = GetTypedProperty(content, type, culture);

                // Fire the converted event
                var convertedArgs = new ConvertedTypeEventArgs
                {
                    Content = content,
                    Converted = instance,
                    ConvertedType = type
                };

                if (convertedType != null)
                {
                    convertedType(convertedArgs);
                }

                EventHandlers.CallConvertedTypeHandler(convertedArgs);

                return convertedArgs.Converted;
            }
        }

        /// <summary>
        /// Returns an object representing the given <see cref="Type"/>.
        /// </summary>
        /// <param name="content">
        /// The <see cref="IPublishedContent"/> to convert.
        /// </param>
        /// <param name="type">
        /// The <see cref="Type"/> of items to return.
        /// </param>
        /// <param name="culture">The <see cref="CultureInfo"/></param>
        /// <returns>
        /// The converted <see cref="Object"/> as the given type.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given type has invalid constructors.
        /// </exception>
        private static object GetTypedProperty(
            IPublishedContent content,
            Type type,
            CultureInfo culture = null)
        {
            // Check if the culture has been set, otherwise use from Umbraco, or fallback to a default
            if (culture == null)
            {
                if (UmbracoContext.Current != null && UmbracoContext.Current.PublishedContentRequest != null)
                {
                    culture = UmbracoContext.Current.PublishedContentRequest.Culture;
                }
                else
                {
                    // Fallback
                    culture = CultureInfo.CurrentCulture;
                }
            }

            // Get the default constructor, parameters and create an instance of the type.
            // Try and return from the cache first. TryGetValue is faster than GetOrAdd.
            ParameterInfo[] constructorParams;
            ConstructorCache.TryGetValue(type, out constructorParams);
            bool hasParameter = false;
            if (constructorParams == null)
            {
                var constructor = type.GetConstructors().OrderBy(x => x.GetParameters().Length).First();
                constructorParams = constructor.GetParameters();
                ConstructorCache.TryAdd(type, constructorParams);
            }

            object instance;
            if (constructorParams.Length == 0)
            {
                // Internally this uses Activator.CreateInstance which is heavily optimized.
                instance = type.GetInstance();
            }
            else if (constructorParams.Length == 1 & constructorParams[0].ParameterType == typeof(IPublishedContent))
            {
                // This extension method is about 7x faster than the native implementation.
                instance = type.GetInstance(content);
                hasParameter = true;
            }
            else
            {
                throw new InvalidOperationException("Type {0} has invalid constructor parameters");
            }

            // Collect all the properties of the given type and loop through writable ones.
            PropertyInfo[] virtualProperties;
            PropertyInfo[] nonVirtualProperties;
            VirtualPropertyCache.TryGetValue(type, out virtualProperties);
            PropertyCache.TryGetValue(type, out nonVirtualProperties);
            if (virtualProperties == null && nonVirtualProperties == null)
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                     .Where(x => x.CanWrite).ToArray();

                // Split out the properties.
                virtualProperties = properties.Where(p => p.IsVirtualAndOverridable()).ToArray();
                nonVirtualProperties = properties.Except(virtualProperties).ToArray();
                VirtualPropertyCache.TryAdd(type, virtualProperties);
                PropertyCache.TryAdd(type, nonVirtualProperties);
            }

            // A dictionary to store lazily invoked values.
            var lazyProperties = new Dictionary<string, Lazy<object>>();

            // If there are any virtual properties we want to lazily invoke them.
            if (virtualProperties != null && virtualProperties.Any())
            {
                foreach (var propertyInfo in virtualProperties)
                {
                    using (DisposableTimer.DebugDuration<object>(string.Format("ForEach Virtual Property ({1} {0})", propertyInfo.Name, content.Id), "Complete"))
                    {
                        // Check for the ignore attribute.
                        var ignoreAttr = propertyInfo.GetCustomAttribute<DittoIgnoreAttribute>();
                        if (ignoreAttr != null)
                        {
                            continue;
                        }

                        // Create a Lazy<object> to deferr returning our value.
                        var deferredPropertyInfo = propertyInfo;
                        var localInstance = instance;
                        lazyProperties.Add(
                            propertyInfo.Name,
                            new Lazy<object>(
                                () =>
                                {
                                    // Get the value from Umbraco.
                                    object propertyValue = GetRawValue(content, culture, deferredPropertyInfo, localInstance);
                                    return GetTypedValue(content, culture, deferredPropertyInfo, propertyValue, localInstance);
                                }));
                    }
                }

                // Create a proxy instance to replace our object.
                LazyInterceptor interceptor = new LazyInterceptor(instance, lazyProperties);
                ProxyFactory factory = new ProxyFactory();

                instance = hasParameter
                    ? factory.CreateProxy(type, interceptor, content)
                    : factory.CreateProxy(type, interceptor);
            }

            // Now loop through and convert non-virtual properties.
            if (nonVirtualProperties != null && nonVirtualProperties.Any())
            {
                foreach (var propertyInfo in nonVirtualProperties)
                {
                    using (DisposableTimer.DebugDuration<object>(string.Format("ForEach Property ({1} {0})", propertyInfo.Name, content.Id), "Complete"))
                    {
                        // Check for the ignore attribute.
                        var ignoreAttr = propertyInfo.GetCustomAttribute<DittoIgnoreAttribute>();
                        if (ignoreAttr != null)
                        {
                            continue;
                        }

                        // Set the value normally.
                        object propertyValue = GetRawValue(content, culture, propertyInfo, instance);
                        var result = GetTypedValue(content, culture, propertyInfo, propertyValue, instance);
                        propertyInfo.SetValue(instance, result, null);
                    }
                }
            }

            return instance;
        }

        /// <summary>
        /// Returns the raw value for the given type and property.
        /// </summary>
        /// <param name="content">The <see cref="IPublishedContent"/> to convert.</param>
        /// <param name="culture">The <see cref="CultureInfo"/></param>
        /// <param name="propertyInfo">The <see cref="PropertyInfo"/> property info associated with the type.</param>
        /// <returns>The <see cref="object"/> representing the Umbraco value.</returns>
        /// <param name="instance">The instance to assign the value to.</param>
        private static object GetRawValue(
            IPublishedContent content,
            CultureInfo culture,
            PropertyInfo propertyInfo,
            object instance)
        {
            // Check the property for an associated value attribute, otherwise fall-back on expected behaviour.
            var valueAttr = propertyInfo.GetCustomAttribute<DittoValueResolverAttribute>(true)
                ?? new UmbracoPropertyAttribute();

            // TODO: Only create one context and share between GetRawValue and SetTypedValue?
            var descriptor = TypeDescriptor.GetProperties(instance)[propertyInfo.Name];
            var context = new PublishedContentContext(content, descriptor);

            // Time custom value-resolver.
            using (DisposableTimer.DebugDuration<object>(string.Format("Custom ValueResolver ({0}, {1})", content.Id, propertyInfo.Name), "Complete"))
            {
                // Get the value from the custom attribute.
                // TODO: Cache these?
                var resolver = (DittoValueResolver)valueAttr.ResolverType.GetInstance();
                return resolver.ResolveValue(context, valueAttr, culture);
            }
        }

        /// <summary>
        /// Set the typed value to the given instance.
        /// </summary>
        /// <param name="content">The <see cref="IPublishedContent"/> to convert.</param>
        /// <param name="culture">The <see cref="CultureInfo"/></param>
        /// <param name="propertyInfo">The <see cref="PropertyInfo"/> property info associated with the type.</param>
        /// <param name="propertyValue">The property value.</param>
        /// <param name="instance">The instance to assign the value to.</param>
        /// <returns>The strong typed converted value for the given property.</returns>
        private static object GetTypedValue(
            IPublishedContent content,
            CultureInfo culture,
            PropertyInfo propertyInfo,
            object propertyValue,
            object instance)
        {
            // Process the value.
            object result = null;
            var propertyType = propertyInfo.PropertyType;
            var typeInfo = propertyType.GetTypeInfo();

            // This should return false against typeof(string) also.
            var propertyIsEnumerableType = propertyType.IsEnumerableType() && typeInfo.GenericTypeArguments.Any();

            // Try any custom type converters first.
            // 1: Check the property.
            // 2: Check any type arguments in generic enumerable types.
            // 3: Check the type itself.
            var converterAttribute =
                propertyInfo.GetCustomAttribute<TypeConverterAttribute>()
                ?? (propertyIsEnumerableType ? typeInfo.GenericTypeArguments.First().GetCustomAttribute<TypeConverterAttribute>(true)
                                             : propertyType.GetCustomAttribute<TypeConverterAttribute>(true));

            if (converterAttribute != null && converterAttribute.ConverterTypeName != null)
            {
                // Time custom conversions.
                using (DisposableTimer.DebugDuration<object>(string.Format("Custom TypeConverter ({0}, {1})", content.Id, propertyInfo.Name), "Complete"))
                {
                    // Get the custom converter from the attribute and attempt to convert.
                    var converterType = Type.GetType(converterAttribute.ConverterTypeName);
                    if (converterType != null)
                    {
                        var converter = converterType.GetDependencyResolvedInstance() as TypeConverter;

                        if (converter != null)
                        {
                            // Create context to pass to converter implementations.
                            // This contains the IPublishedContent and the currently converting property descriptor.
                            var descriptor = TypeDescriptor.GetProperties(instance)[propertyInfo.Name];
                            var context = new PublishedContentContext(content, descriptor);

                            Type propertyValueType = null;
                            if (propertyValue != null)
                            {
                                propertyValueType = propertyValue.GetType();
                            }

                            // We're deliberately passing null.
                            // ReSharper disable once AssignNullToNotNullAttribute
                            if (converter.CanConvertFrom(context, propertyValueType))
                            {
                                object converted = converter.ConvertFrom(context, culture, propertyValue);

                                if (converted != null)
                                {
                                    // Handle Typeconverters returning single objects when we want an IEnumerable.
                                    // Use case: Someone selects a folder of images rather than a single image with the media picker.
                                    var convertedType = converted.GetType();

                                    if (propertyIsEnumerableType)
                                    {
                                        var parameterType = typeInfo.GenericTypeArguments.First();

                                        // Some converters return an IEnumerable so we check again.
                                        if (!convertedType.IsEnumerableType())
                                        {
                                            // Using 'Cast' to convert the type back to IEnumerable<T>.
                                            object enumerablePropertyValue = EnumerableInvocations.Cast(
                                                                                parameterType,
                                                                                converted.YieldSingleItem());

                                            result = enumerablePropertyValue;
                                        }
                                        else
                                        {
                                            // Nothing is strong typed anymore.
                                            result = EnumerableInvocations.Cast(parameterType, (IEnumerable)converted);
                                        }
                                    }
                                    else
                                    {
                                        // Return single expected items from converters returning an IEnumerable.
                                        // Check for string.
                                        if (convertedType.IsEnumerableType() && convertedType.GenericTypeArguments.Any())
                                        {
                                            // Use 'FirstOrDefault' to convert the type back to T.
                                            result = EnumerableInvocations.FirstOrDefault(propertyType, (IEnumerable)converted);
                                        }
                                        else
                                        {
                                            result = converted;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (propertyInfo.PropertyType == typeof(HtmlString))
            {
                // Handle Html strings so we don't have to set the attribute.
                var converterType = typeof(DittoHtmlStringConverter);
                var converter = converterType.GetDependencyResolvedInstance() as TypeConverter;

                if (converter != null)
                {
                    // This contains the IPublishedContent and the currently converting property descriptor.
                    var descriptor = TypeDescriptor.GetProperties(instance)[propertyInfo.Name];
                    var context = new PublishedContentContext(content, descriptor);

                    Type propertyValueType = null;
                    if (propertyValue != null)
                    {
                        propertyValueType = propertyValue.GetType();
                    }

                    // We're deliberately passing null.
                    // ReSharper disable once AssignNullToNotNullAttribute
                    if (converter.CanConvertFrom(context, propertyValueType))
                    {
                        result = converter.ConvertFrom(context, culture, propertyValue);
                    }
                }
            }
            else if (propertyInfo.PropertyType.IsInstanceOfType(propertyValue))
            {
                // Simple types
                result = propertyValue;
            }
            else
            {
                using (DisposableTimer.DebugDuration<object>(string.Format("TypeConverter ({0}, {1})", content.Id, propertyInfo.Name), "Complete"))
                {
                    var convert = propertyValue.TryConvertTo(propertyInfo.PropertyType);
                    if (convert.Success)
                    {
                        result = convert.Result;
                    }
                }
            }

            return result;
        }
    }
}