﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ToSic.Eav.Interfaces
{
    public interface ISystemConfiguration
    {
        string DbConnectionString { get; }
    }
}