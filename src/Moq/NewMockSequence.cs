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
		internal SequenceException(Times times, int executedCount, ISetup setup) : 
			base($"{times.GetExceptionMessage(executedCount)}{(setup == null ? "" : $"{setup}")}") { }
	}

	/// <summary>
	/// 
	/// </summary>
	public class VerifiableSetup
	{
		private MockSequenceSetup sequenceSetup;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sequenceSetup"></param>
		public VerifiableSetup(MockSequenceSetup sequenceSetup)
		{
			this.sequenceSetup = sequenceSetup;
		}

		/// <summary>
		/// 
		/// </summary>
		public void Verify()
		{
			VerifySequenceSetup(sequenceSetup);
		}

		private void VerifySequenceSetup(MockSequenceSetup sequenceSetup)
		{
			Verify(sequenceSetup.Times, sequenceSetup.ExecutionCount, sequenceSetup.Setup);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="times"></param>
		public void Verify(Times times)
		{
			Verify(times, sequenceSetup.TotalExecutionCount, sequenceSetup.Setup);
		}

		/// <summary>
		/// 
		/// </summary>
		public void VerifyAll()
		{
			foreach (var sequenceSetup in sequenceSetup.TrackedSetup.MockSequenceSetups)
			{
				VerifySequenceSetup(sequenceSetup);
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="times"></param>
		public void VerifyAll(Times times)
		{
			Verify(times, sequenceSetup.TrackedSetup.TotalExecutions());
		}

		private void Verify(Times times, int executionCount, ISetup setup = null)
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
	public class MockSequenceSetup : SequenceSetupBase
	{
		private int executionCount;
		internal int ExecutionCount
		{
			get
			{
				return executionCount;
			}
		}

		internal Times Times { get; set; }

		internal int TotalExecutionCount { get; set; }

		internal TrackedMockSequenceSetup TrackedSetup { get => (TrackedMockSequenceSetup)TrackedSetupBase; }

		internal void Executed()
		{
			executionCount++;
			TotalExecutionCount++;
		}

		internal MockSequenceSetup GetNextConsecutiveTrackedSetup(bool cyclic)
		{
			return TrackedSetup.GetNextConsecutiveTrackedSetup(this.SetupIndex,cyclic);
		}

		internal int GetNextSequenceSetupIndex(int currentSetupIndex)
		{
			return this.TrackedSetup.GetNextSequenceSetupIndex(currentSetupIndex);
		}

		internal void ResetForCyclical()
		{
			executionCount = 0;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public class TrackedMockSequenceSetup : TrackedSetupBase<MockSequenceSetup>
	{
		/// <summary>
		/// 
		/// </summary>
		/// <param name="sequenceSetup"></param>
		public TrackedMockSequenceSetup(MockSequenceSetup sequenceSetup) : base(sequenceSetup) { }
		internal IEnumerable<MockSequenceSetup> MockSequenceSetups => SequenceSetups;
		internal MockSequenceSetup GetNextConsecutiveTrackedSetup(int relativeTo,bool cyclic)
		{
			if (relativeTo == SequenceSetups.Count - 1)
			{
				if (cyclic)
				{
					return SequenceSetups.SingleOrDefault(s => s.SetupIndex == 0);
				}
				return null;
			}

			return SequenceSetups.SingleOrDefault(s => s.SetupIndex == relativeTo + 1);
		}

		internal int TotalExecutions()
		{
			return SequenceSetups.Sum(ss => ss.TotalExecutionCount);
		}

		internal int GetNextSequenceSetupIndex(int currentSetupIndex)
		{
			int firstSequenceSetupIndex = -1;
			int nextSequenceSetupIndex = -1;
			foreach (var sequenceSetup in SequenceSetups)
			{
				var sequenceSetupIndex = sequenceSetup.SetupIndex;
				if (firstSequenceSetupIndex == -1)
				{
					firstSequenceSetupIndex = sequenceSetupIndex;
				}
				if (sequenceSetupIndex > currentSetupIndex)
				{
					nextSequenceSetupIndex = sequenceSetupIndex;
					break;
				}
			}

			if (nextSequenceSetupIndex != -1)
			{
				return nextSequenceSetupIndex;
			}
			return firstSequenceSetupIndex;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public sealed class NewMockSequence : MockSequenceBase<MockSequenceSetup, TrackedMockSequenceSetup>
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
		private int CurrentSequenceSetupIndex
		{
			get
			{
				return currentSequenceSetupIndex;
			}
			set
			{
				if (value < currentSequenceSetupIndex)
				{
					foreach(var sequenceSetup in SequenceSetups)
					{
						sequenceSetup.ResetForCyclical();
					}
				}
				currentSequenceSetupIndex = value;
			}
		}

		

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
				sequenceSetup.Times = t;
				verifiableSetup = new VerifiableSetup(sequenceSetup);
			});
			return verifiableSetup;
		}

		private bool SetupBeforeCurrentCondition(MockSequenceSetup newSequenceSetup, int newIndex)
		{
			if (Cyclical)
			{
				ConfirmSequenceSetupsSatisfied(SequenceSetups.Count);
				CurrentSequenceSetupIndex = newIndex;
				ConfirmSequenceSetup(newSequenceSetup);
			}
			else
			{
				if (strict) // to be determined
				{
					throw new StrictSequenceException() { UnmatchedSequenceInvocations = UnmatchedInvocations() };
				}
			}

			return true;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sequenceSetup"></param>
		/// <returns></returns>
		protected override bool Condition(MockSequenceSetup sequenceSetup)
		{
			var condition = true;
			var currentSequenceSetup = SequenceSetups[CurrentSequenceSetupIndex];

			if (currentSequenceSetup.TrackedSetup == sequenceSetup.TrackedSetup)
			{
				if (currentSequenceSetup == sequenceSetup)
				{
					ConfirmSequenceSetup(currentSequenceSetup);
				}
				else
				{
					return false; // the one we are interested in will come along soon
				}
			}
			else
			{
				ConfirmSequenceSetupSatisfied(currentSequenceSetup);

				var nextSequenceSetupIndex = sequenceSetup.GetNextSequenceSetupIndex(currentSequenceSetup.SetupIndex);
				if (nextSequenceSetupIndex != sequenceSetup.SetupIndex)
				{
					return false; // there will be another along
				}

				if (nextSequenceSetupIndex > CurrentSequenceSetupIndex)
				{
					ConfirmSequenceSetupsSatisfied(nextSequenceSetupIndex);
					ConfirmSequenceSetup(SequenceSetups[nextSequenceSetupIndex]);
					CurrentSequenceSetupIndex = nextSequenceSetupIndex;
				}
				else
				{
					condition = SetupBeforeCurrentCondition(sequenceSetup, nextSequenceSetupIndex);
				}
			}

			return condition;
		}

		private void ConfirmSequenceSetupSatisfied(MockSequenceSetup sequenceSetup)
		{
			var times = sequenceSetup.Times;
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
			for (var j = CurrentSequenceSetupIndex + 1; j < upToIndex; j++)
			{
				ConfirmSequenceSetupSatisfied(SequenceSetups[j]);
			}
		}

		private void AtLeastInvoked(MockSequenceSetup sequenceSetup, int atLeast)
		{
			if (atLeast == sequenceSetup.ExecutionCount)
			{
				var nextConsecutiveTrackedSetup = sequenceSetup.GetNextConsecutiveTrackedSetup(Cyclical);
				if(nextConsecutiveTrackedSetup != null)
				{
					CurrentSequenceSetupIndex = nextConsecutiveTrackedSetup.SetupIndex;
				}
				
			}
		}

		private bool ConfirmAtMostNotExceeded(MockSequenceSetup sequenceSetup, int atMost)
		{
			if (atMost == sequenceSetup.ExecutionCount)
			{
				var nextConsecutiveTrackedSetup = sequenceSetup.GetNextConsecutiveTrackedSetup(Cyclical);
				if (nextConsecutiveTrackedSetup != null)
				{
					CurrentSequenceSetupIndex = nextConsecutiveTrackedSetup.SetupIndex;
					return true;
				}
			}
			return sequenceSetup.Times.Validate(sequenceSetup.ExecutionCount);
		}

		private bool ConfirmExactTimesNotExceeded(MockSequenceSetup sequenceSetup, int exactTimes)
		{
			
			if (exactTimes == sequenceSetup.ExecutionCount)
			{
				var nextConsecutiveTrackedSetup = sequenceSetup.GetNextConsecutiveTrackedSetup(Cyclical);
				if (nextConsecutiveTrackedSetup != null)
				{
					CurrentSequenceSetupIndex = nextConsecutiveTrackedSetup.SetupIndex;
					return true;
				}
			}
			return sequenceSetup.ExecutionCount <= exactTimes;
		}

		private void ConfirmSequenceSetup(MockSequenceSetup sequenceSetup)
		{
			var times = sequenceSetup.Times;
			times.Deconstruct(out int from, out int to);
			var kind = times.GetKind();
			sequenceSetup.Executed();

			var shouldThrow = false;
			switch (kind)
			{
				case Times.Kind.Never:
					shouldThrow = true;
					break;
				case Times.Kind.Exactly:
				case Times.Kind.Once:
					shouldThrow = !ConfirmExactTimesNotExceeded(sequenceSetup, from);
					break;
				case Times.Kind.AtLeast:
				case Times.Kind.AtLeastOnce:
					AtLeastInvoked(sequenceSetup, from);
					break;
				case Times.Kind.AtMost:
				case Times.Kind.AtMostOnce:
					shouldThrow = !ConfirmAtMostNotExceeded(sequenceSetup, to);
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
			for(var i = CurrentSequenceSetupIndex; i < SequenceSetups.Count; i++)
			{
				ConfirmSequenceSetupSatisfied(SequenceSetups[i]);
			}
		}
	}

}
