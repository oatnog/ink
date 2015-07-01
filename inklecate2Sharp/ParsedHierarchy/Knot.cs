﻿using System.Collections.Generic;

namespace Inklewriter.Parsed
{
	public class Knot : FlowBase
	{
        public override FlowLevel flowLevel { get { return FlowLevel.Knot; } }

        public Knot (string name, List<Parsed.Object> topLevelObjects, List<string> parameterNames) : base(name, topLevelObjects, parameterNames)
		{
		}
            
	}
}
