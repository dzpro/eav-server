﻿using ToSic.SexyContent.Environment.Interfaces;

namespace ToSic.Eav.Apps.Environment
{
    // todo: probably replace with DimensionDefinition
    public class TempTempCulture: ITempCulture
    {
        public TempTempCulture(string code, string text, bool active)
        {
            Key = code;
            Text = text;
            Active = active;
        }

        public string Key { get;  }
        public string Text { get;  }
        public bool Active { get;  }
        
    }
}