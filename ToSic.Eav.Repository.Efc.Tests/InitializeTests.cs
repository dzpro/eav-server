﻿using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Practices.Unity;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToSic.Eav.Implementations.UserInformation;
using ToSic.Eav.Implementations.ValueConverter;
using ToSic.Eav.ImportExport.Interfaces;
using ToSic.Eav.Persistence.Efc.Diagnostics;
using ToSic.Eav.Persistence.Efc.Models;
using ToSic.Eav.Repository.Efc.Tests.Mocks;

namespace ToSic.Eav.Repository.Efc.Tests
{
    [TestClass]
    class InitializeTests
    {
        static string conStr = @"Data Source=(local)\SQLEXPRESS;Initial Catalog=""2flex 2Sexy Content"";Integrated Security=True;";
        [AssemblyInitialize()]
        public static void AssemblyInit(TestContext context)
        {

            //ConfigureUnityDI();
            ConfigureEfcDi();
            //AddSqlLogger();
            Factory.Debug = true;
        }

        private static void ConfigureEfcDi()
        {
            Configuration.SetConnectionString(conStr);
            Factory.ActivateNetCoreDi(sc =>
            {
                //var cont = Factory.CreateContainer(); //.Container;
                sc.AddTransient<IImportExportEnvironment, ImportExportEnvironmentMock>();
                sc.AddTransient<IEavValueConverter, NeutralValueConverter>();//new InjectionConstructor());
                sc.AddTransient<IEavUserInformation, NeutralEavUserInformation>();//new InjectionConstructor());

                new DependencyInjection().ConfigureNetCoreContainer(sc);

                return sc;
            });

        }

        private static void AddSqlLogger()
        {
            // try to add logger
            var db = Factory.Resolve<EavDbContext>();
            var serviceProvider = db.GetInfrastructure();
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            loggerFactory.AddProvider(new EfCoreLoggerProvider());
        }

        private static void ConfigureUnityDI()
        {
            //var conStr = @"Data Source=(local)\SQLEXPRESS;Initial Catalog=""2flex 2Sexy Content"";Integrated Security=True;";
            Configuration.SetConnectionString(conStr);
            var cont = Factory.CreateContainer();//.Container;

            cont.RegisterType<IImportExportEnvironment, ImportExportEnvironmentMock>();
            cont.RegisterType<IEavValueConverter, NeutralValueConverter>(new InjectionConstructor());
            cont.RegisterType<IEavUserInformation, NeutralEavUserInformation>(new InjectionConstructor());

            new DependencyInjection().ConfigureDefaultMappings(cont);
        }
    }
}
