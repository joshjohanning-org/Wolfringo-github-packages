﻿namespace TehGM.Wolfringo
{
    public enum WolfGroupCapabilities
    {
        // values borrowed from https://github.com/dewwalters/Wolf.Net/blob/master/Wolf.Net/Enums/Capabilities.cs
        NonGroupRequest = -2,
        NotMember = -1,
        User = 0,
        Admin = 1,
        Mod = 2,
        Banned = 4,
        Silenced = 8,
        Owner = 32
    }
}
