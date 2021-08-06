// Copyright (c) 2007, Clarius Consulting, Manas Technology Solutions, InSTEDD, and Contributors.
// All rights reserved. Licensed under the BSD 3-Clause License; see License.txt.

namespace Moq
{
	/// <summary>
	/// 
	/// </summary>
	internal class SequenceInvocation : ISequenceInvocation
	{
		public SequenceInvocation(Mock mock, IInvocation invocation)
		{
			Mock = mock;
			Invocation = invocation;

		}
		/// <summary>
		/// 
		/// </summary>
		public Mock Mock { get; }
		/// <summary>
		/// 
		/// </summary>
		public IInvocation Invocation { get; }
		/// <summary>
		/// 
		/// </summary>
		public bool Matched { get; set; }
	}

}
