/*
    ViVe - Windows feature configuration library
    Copyright (C) 2019-2025  @thebookisclosed

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
 */

using System;

namespace Albacore.ViVe.Exceptions
{
    public class FeaturePropertyOverflowException : Exception
    {
        public string PropertyName { get; }
        public uint MaxAllowedValue { get; }

        public FeaturePropertyOverflowException(string propertyName, uint maxAllowedValue)
            : base($"属性 {propertyName} 的取值超出允许范围，最大允许值为 {maxAllowedValue}。")
        {
            PropertyName = propertyName;
            MaxAllowedValue = maxAllowedValue;
        }
    }
}
