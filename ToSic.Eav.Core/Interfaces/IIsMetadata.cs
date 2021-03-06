﻿using System;
using System.Web.Script.Serialization;

namespace ToSic.Eav.Interfaces
{
	/// <summary>
	/// Represents an object which is metadata for some other object
	/// </summary>
	public interface IIsMetadata
	{
        [ScriptIgnore]
        bool IsMetadata { get; }

        int TargetType { get; }

        string KeyString { get; }

        int? KeyNumber { get; }

        Guid? KeyGuid { get; }

    }

}