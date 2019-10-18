// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Net;

//using Arriba.Monitoring;
//using Arriba.Server.Owin;

//using Microsoft.Owin.Builder;
//using Microsoft.Owin.Hosting;

//using Owin;

namespace Arriba.Server
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    internal class Program
    {
        private const int DefaultPort = 42784;

        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });

        //private static void Main(string[] args)
        //{
        //    Console.WriteLine("Arriba Local Server\r\n");

        //    //Configuration c = Configuration.GetConfigurationForArgs(args);
        //    //int portNumber = c.GetConfigurationInt("port", DefaultPort);

        //    //// Write trace messages to console if /trace is specified 
        //    //if (c.GetConfigurationBool("trace", Debugger.IsAttached))
        //    //{
        //    //    EventPublisher.AddConsumer(new ConsoleEventConsumer());
        //    //}

        //    //// Always log to CSV
        //    //EventPublisher.AddConsumer(new CsvEventConsumer());

        //    //using (var app = WebApp.Start<SelfHostArribaOwinStartup>(String.Format("http://*:{0}/", portNumber)))
        //    //{
        //    //    Console.WriteLine("Running... Press any key to exit.");
        //    //    Console.ReadKey();
        //    //}

        //    //Console.WriteLine("Exiting.");
        //    //Environment.Exit(0);
        //}
    }

    internal class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
        }

        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseEndpoints(ep =>
            {
                ep.MapControllers();
            });
        }
    }

    //public class SelfHostArribaOwinStartup : ArribaOwinStartup
    //{
    //    public override void Configuration(IAppBuilder app)
    //    {
    //        base.Configuration(app);

    //        // Enable self host NTLM authentication 
    //        var listener = (HttpListener)app.Properties[typeof(HttpListener).FullName];
    //        listener.AuthenticationSchemes = AuthenticationSchemes.Negotiate;
    //    }
    //}
}

