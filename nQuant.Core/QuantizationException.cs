﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace nQuant
{
    [Serializable]
    public class QuantizationException : ApplicationException
    {
        public QuantizationException(string message) : base(message)
        {

        }
    }
}
