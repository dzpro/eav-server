﻿using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("EAV Library")]
[assembly: AssemblyDescription("Entity-Attribute-Value (EAV) Library")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("2sic Internet Solutions GmbH")]
[assembly: AssemblyProduct("2sic EAV")]
[assembly: AssemblyCopyright("Copyright © 2sic 2012-2017")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("04df84dd-2134-45a7-97be-2d77ad1c1878")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers 
// by using the '*' as shown below:
[assembly: AssemblyVersion("4.5.0.*")]
// [assembly: AssemblyFileVersion("4.5.0.*")]

// This is needed for unit testing
[assembly: InternalsVisibleTo("ToSic.Eav.UnitTests")]
//[assembly: InternalsVisibleTo("ToSic.SexyContent")]
[assembly: InternalsVisibleTo("ToSic.Eav.Apps")]