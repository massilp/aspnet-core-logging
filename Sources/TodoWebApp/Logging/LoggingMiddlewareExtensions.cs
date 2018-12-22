﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;

namespace TodoWebApp.Logging
{
    /// <summary>
    /// Contains extension methods applicable to <see cref="IApplicationBuilder"/> instances.
    /// </summary>
    public static class LoggingMiddlewareExtensions
    {
        /// <summary>
        /// Adds middleware for logging the current <see cref="HttpContext"/> object.
        /// </summary>
        /// <param name="applicationBuilder"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseHttpLogging(this IApplicationBuilder applicationBuilder)
        {
            if (applicationBuilder == null)
            {
                throw new ArgumentNullException(nameof(applicationBuilder));
            }

            applicationBuilder.UseMiddleware<LoggingMiddleware>();
            return applicationBuilder;
        }
    }
}
