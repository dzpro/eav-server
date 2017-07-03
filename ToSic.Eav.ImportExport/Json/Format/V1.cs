﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ToSic.Eav.ImportExport.Json.Format
{

    internal class JsonHeader { public int V = 1; }

    internal class JsonMetadataFor
    {
        public string Target;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string String;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public Guid? Guid;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? Number;
    }

    internal class JsonType { public string Name, Id; }

    internal class JsonFormat
    {
        public JsonHeader _ = new JsonHeader();
        public JsonEntity Entity;
    }

    internal class JsonEntity
    {
        public int Id;
        public Guid Guid;
        public JsonType Type;
        public JsonAttributes Attributes;
        public string Owner;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JsonMetadataFor For;
    }

    internal class JsonAttributes
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, string>> String;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, string>> Hyperlink;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, string>> Custom;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, List<Guid?>>> Entity;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, decimal?>> Number;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, DateTime?>> DateTime;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, bool?>> Boolean;
    }
}