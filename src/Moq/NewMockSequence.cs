using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Moq
{
	/// <summary>
	/// 
	/// </summary>
	public partial class SequenceException : Exception
	{
		internal SequenceException(Times times, int executedCount, ISetup setup) : base($"{setup} {times.GetExceptionMessage(executedCount)}") { }
	}

	/// <summary>
	/// 
	/// </summary>
	public class VerifiableSetup
	{
		private ISequenceSetup<Times> sequenceSetup;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sequenceSetup"></param>
		public VerifiableSetup(ISequenceSetup<Times> sequenceSetup)
		{
			this.sequenceSetup = sequenceSetup;
		}

		/// <summary>
		/// 
		/// </summary>
		public void Verify()
		{
			Verify(sequenceSetup.Context, sequenceSetup.ExecutionCount, sequenceSetup.Setup);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="times"></param>
		public void Verify(Times times)
		{
			Verify(times, sequenceSetup.ExecutionCount,sequenceSetup.Setup);
		}

		private void Verify(Times times, int executionCount, ISetup setup)
		{
			var success = times.Validate(executionCount);
			if (!success)
			{
				throw new SequenceException(times, executionCount, setup);
			}
		}

	}

	/// <summary>
	/// 
	/// </summary>
	public sealed class NewMockSequence : MockSequenceBase<Times>
	{
		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		public static Times OptionalTimes()
		{
			return Times.Between(0, int.MaxValue, Range.Inclusive);
		}

		private int currentSequenceSetupIndex;

		/// <summary>
		/// 
		/// </summary>
		public bool Cyclical { get; set; }
		/// <summary>
		/// 
		/// </summary>
		/// <param name="strict"></param>
		/// <param name="mocks"></param>
		public NewMockSequence(bool strict, params Mock[] mocks) : base(strict, mocks) { }

		/// <summary>
		/// 
		/// </summary>
		/// <param name="setup"></param>
		/// <param name="times"></param>
		/// <returns></returns>
		public VerifiableSetup Setup(Action setup, Times? times = null)
		{
			Times t = times ?? Times.Once();
			VerifiableSetup verifiableSetup = null;
			base.InterceptSetup(setup, (sequenceSetup) =>
			{
				verifiableSetup = new VerifiableSetup(sequenceSetup);
				return t;
			});
			return verifiableSetup;
		}

		private bool SequenceCompletedAndLooseCondition()
		{
			var satisfied = false;
			if (currentSequenceSetupIndex >= SequenceSetups.Count)
			{
				if (Cyclical)
				{
					currentSequenceSetupIndex = 0;
				}
				else
				{
					if (strict)
					{
						throw new StrictSequenceException() { UnmatchedSequenceInvocations = UnmatchedInvocations() };
					}
					satisfied = true;
				}
			}
			return satisfied;
		}

		private bool SequenceCondition(ISequenceSetup<Times> sequenceSetup)
		{
			var condition = true;
			var currentSequenceSetup = SequenceSetups[currentSequenceSetupIndex];

			if (currentSequenceSetup == sequenceSetup)
			{
				ConfirmSequenceSetup(currentSequenceSetup);
			}
			else
			{
				ConfirmSequenceSetupSatisfied(currentSequenceSetup);
				var nextSequenceSetupIndex = SequenceSetups.ToList().IndexOf(sequenceSetup);
				if (nextSequenceSetupIndex > currentSequenceSetupIndex)
				{
					ConfirmSequenceSetupsSatisfied(nextSequenceSetupIndex);
					ConfirmSequenceSetup(SequenceSetups[nextSequenceSetupIndex]);
					currentSequenceSetupIndex = nextSequenceSetupIndex;
				}
				else
				{
					condition = SetupBeforeCurrentCondition(sequenceSetup, nextSequenceSetupIndex);
				}
			}

			return condition;
		}

		private bool SetupBeforeCurrentCondition(ISequenceSetup<Times> newSequenceSetup, int newIndex)
		{
			if (Cyclical)
			{
				ConfirmSequenceSetupsSatisfied(SequenceSetups.Count);
				currentSequenceSetupIndex = newIndex;
				ConfirmSequenceSetup(newSequenceSetup);
			}
			else
			{
				if (strict)
				{
					throw new StrictSequenceException() { UnmatchedSequenceInvocations = UnmatchedInvocations() };
				}
				// should increment currentSequenceSetupIndex beyond the end ?
			}

			return true;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sequenceSetup"></param>
		/// <param name="invocationIndex"></param>
		/// <returns></returns>
		protected override bool Condition(ISequenceSetup<Times> sequenceSetup, int invocationIndex)
		{
			// given that is going to be called for all with same expression ( in order added )
			// and want to determine which applies to
			// all have is the invocation index
			return SequenceCompletedAndLooseCondition() || SequenceCondition(sequenceSetup);
		}



		//private int GetNextSequenceSetupIndex(ISequenceSetup<Times> trackedSetup)
		//{
		//	for (var i = currentSequenceSetupIndex + 1; i < SequenceSetups.Count; i++)
		//	{
		//		var sequenceSetup = SequenceSetups[i];
		//		if (sequenceSetup.TrackedSetup == trackedSetup)
		//		{
		//			return i;
		//		}
		//	}
		//	return -1;
		//}

		//private int GetSequenceSetupIndex(ITrackedSetup<Times> trackedSetup)
		//{
		//	for (var i = 0; i < SequenceSetups.Count; i++)
		//	{
		//		var sequenceSetup = SequenceSetups[i];
		//		if (sequenceSetup.TrackedSetup == trackedSetup)
		//		{
		//			return i;
		//		}
		//	}
		//	return -1;
		//}

		private void ConfirmSequenceSetupSatisfied(ISequenceSetup<Times> sequenceSetup)
		{
			var times = sequenceSetup.Context;
			var kind = times.GetKind();
			switch (kind)
			{
				case Times.Kind.Never:
				case Times.Kind.AtMost:
				case Times.Kind.AtMostOnce:
					break;
				case Times.Kind.Exactly:
				case Times.Kind.Once:
				case Times.Kind.BetweenExclusive:
				case Times.Kind.BetweenInclusive:
				case Times.Kind.AtLeast:
				case Times.Kind.AtLeastOnce:
					if (!times.Validate(sequenceSetup.ExecutionCount))
					{
						throw new SequenceException(times,sequenceSetup.ExecutionCount,sequenceSetup.Setup);
					}
					break;
			}
		}

		private void ConfirmSequenceSetupsSatisfied(int upToIndex)
		{
			for (var j = currentSequenceSetupIndex + 1; j < upToIndex; j++)
			{
				ConfirmSequenceSetupSatisfied(SequenceSetups[j]);
			}
		}

		private void ConfirmSequenceSetup(ISequenceSetup<Times> sequenceSetup)
		{
			var times = sequenceSetup.Context;
			times.Deconstruct(out int _, out int to);
			var kind = times.GetKind();
			sequenceSetup.ExecutionCount++;
			var shouldThrow = false;
			switch (kind)
			{
				case Times.Kind.Never:
					shouldThrow = true;
					break;
				case Times.Kind.Exactly:
				case Times.Kind.Once:
					if (times.Validate(sequenceSetup.ExecutionCount))
					{
						currentSequenceSetupIndex++;
					}
					break;
				case Times.Kind.AtLeast:
				case Times.Kind.AtLeastOnce:
					// we do not shift
					break;
				case Times.Kind.AtMost:
				case Times.Kind.AtMostOnce:
					shouldThrow = !times.Validate(sequenceSetup.ExecutionCount);
					break;
				case Times.Kind.BetweenExclusive:
				case Times.Kind.BetweenInclusive:
					if (sequenceSetup.ExecutionCount > to)
					{
						shouldThrow = true;
					}
					break;
			}

			if (shouldThrow)
			{
				throw new SequenceException(times, sequenceSetup.ExecutionCount, sequenceSetup.Setup);
			}
		}
	
		/// <summary>
		/// 
		/// </summary>
		protected override void VerifyImpl()
		{
			for(var i = currentSequenceSetupIndex; i < SequenceSetups.Count; i++)
			{
				ConfirmSequenceSetupSatisfied(SequenceSetups[i]);
			}
		}
	}

}
