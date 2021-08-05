// Copyright (c) 2007, Clarius Consulting, Manas Technology Solutions, InSTEDD, and Contributors.
// All rights reserved. Licensed under the BSD 3-Clause License; see License.txt.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Moq
{
	/// <summary>
	/// 
	/// </summary>
	public abstract class SequenceSetupBase
	{
		/// <summary>
		/// 
		/// </summary>
		protected internal object TrackedSetupBase { get; set; }

		/// <summary>
		/// 
		/// </summary>
		public int SetupIndex { get; set; }

		/// <summary>
		/// 
		/// </summary>
		public ISetup Setup => SetupInternal;
		internal Setup SetupInternal { get; set; }

	}

	/// <summary>
	/// 
	/// </summary>
	/// <typeparam name="TSequenceSetup"></typeparam>
	public abstract class TrackedSetupBase<TSequenceSetup> where TSequenceSetup : SequenceSetupBase
	{
		private readonly List<TSequenceSetup> sequenceSetupsInternal = new List<TSequenceSetup>();

		/// <summary>
		/// 
		/// </summary>
		protected IReadOnlyList<TSequenceSetup> SequenceSetups => sequenceSetupsInternal;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sequenceSetup"></param>
		public TrackedSetupBase(TSequenceSetup sequenceSetup)
		{
			sequenceSetupsInternal.Add(sequenceSetup);
			InvocationShape = sequenceSetup.SetupInternal.Expectation;
			sequenceSetup.TrackedSetupBase = this;
		}
		
		internal void AddSequenceSetup(TSequenceSetup sequenceSetup)
		{
			sequenceSetupsInternal.Add(sequenceSetup);
			sequenceSetup.TrackedSetupBase = this;
		}
		internal InvocationShape InvocationShape { get; }
	}

	/// <summary>
	/// 
	/// </summary>
	public class SequenceInvocation
	{
		/// <summary>
		/// 
		/// </summary>
		public Mock Mock { get; set; }
		/// <summary>
		/// 
		/// </summary>
		public IInvocation Invocation { get; set; }
		/// <summary>
		/// 
		/// </summary>
		public bool Matched { get; set; }
	}

	/// <summary>
	/// 
	/// </summary>
	public partial class SequenceException : Exception
	{
		internal SequenceException() { }
		internal SequenceException(string message) : base(message) { }
	}

	/// <summary>
	/// 
	/// </summary>
	public class StrictSequenceException : SequenceException
	{
		/// <summary>
		/// 
		/// </summary>
		public IEnumerable<SequenceInvocation> UnmatchedSequenceInvocations { get; set; }
	}

	/// <summary>
	/// 
	/// </summary>     
	public abstract class MockSequenceBase<TSequenceSetup,TTrackedSetup> 
		where TSequenceSetup : SequenceSetupBase 
		where TTrackedSetup : TrackedSetupBase<TSequenceSetup>
	{
		/// <summary>
		/// 
		/// </summary>
		protected readonly bool strict;
		private int setupCount = -1;
		private readonly Mock[] mocks;
		private readonly List<Mock> listenedToMocks = new List<Mock>(); // later listener that contains the invocations ?
		internal readonly List<SequenceInvocation> sequenceInvocations = new List<SequenceInvocation>();

		private readonly List<TTrackedSetup> trackedSetups = new List<TTrackedSetup>();
		private readonly List<TSequenceSetup> sequenceSetups = new List<TSequenceSetup>();
		/// <summary>
		/// 
		/// </summary>
		protected IReadOnlyList<TSequenceSetup> SequenceSetups => sequenceSetups;
		private readonly List<ISetup> allSetups = new List<ISetup>();
		
		
		/// <summary>
		/// 
		/// </summary>
		/// <param name="strict"></param>
		/// <param name="mocks"></param>
		public MockSequenceBase(bool strict,params Mock[] mocks)
		{
			if(mocks.Length == 0)
			{
				throw new ArgumentException("No mocks", nameof(mocks));
			}
			this.mocks = mocks;
			this.strict = strict;
			ListenForInvocations();
		}

		private void ListenForInvocations()
		{
			ListenForInvocations(mocks);
		}

		private void ListenForInvocations(IEnumerable<Mock> mocks)
		{
			foreach (var mock in mocks)
			{
				ListenForInvocation(mock);
			}
		}

		private void ListenForInvocation(Mock mock)
		{
			ListenForInvocations(mock.MutableSetups.Where(s => s.InnerMock != null).Select(s=>s.InnerMock));
			if (!listenedToMocks.Contains(mock))
			{
				mock.AddInvocationListener(invocation => sequenceInvocations.Add(new SequenceInvocation { Invocation = invocation, Mock = mock }));
				listenedToMocks.Add(mock);
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="setup"></param>
		/// <param name="sequenceSetupCallback"></param>
		protected void InterceptSetup(Action setup, Action<TSequenceSetup> sequenceSetupCallback)
		{
			setupCount++;
			List<List<SetupWithDepth>> allSetupsBefore = mocks.Select(m => MoqSetupFinder.GetAllSetups(m)).ToList();
			setup();
			List<List<SetupWithDepth>> allSetupsAfter = mocks.Select(m => MoqSetupFinder.GetAllSetups(m)).ToList();
			for(var i = 0; i < allSetupsBefore.Count; i++)
			{
				var result = allSetupsBefore[i].NewSetups(allSetupsAfter[i]);
				if (!result.NoChange)
				{
					var terminalSetup = result.TerminalSetup;
					var setupsExceptTerminal = result.NewSetups.Except(new SetupWithDepth[] { terminalSetup });
					allSetups.AddRange(result.NewSetups.Select(sd => sd.Setup));
					ListenForInvocations(result.NewSetups.Select(s => s.Setup.Mock));

					var sequenceSetup = (TSequenceSetup)Activator.CreateInstance(typeof(TSequenceSetup));
					sequenceSetup.SetupIndex = setupCount;
					sequenceSetup.SetupInternal = terminalSetup.Setup;
					TrackSetup(sequenceSetup);
					sequenceSetupCallback(sequenceSetup);
					SetCondition(sequenceSetup,terminalSetup.Setup);
					sequenceSetups.Add(sequenceSetup);

					return;
				}

			}

			throw new ArgumentException("No setup performed",nameof(setup));
		}

		

		private void TrackSetup(TSequenceSetup newSequenceSetup)
		{
			var invocationShape = newSequenceSetup.SetupInternal.Expectation;
			var trackedSetup = trackedSetups.SingleOrDefault(ts => ts.InvocationShape.Equals(invocationShape));
			if (trackedSetup == null)
			{
				trackedSetup = (TTrackedSetup)Activator.CreateInstance(typeof(TTrackedSetup),newSequenceSetup);
				trackedSetups.Add(trackedSetup);
			}
			else
			{
				trackedSetup.AddSequenceSetup(newSequenceSetup);
			}

		}

		private void SetCondition(TSequenceSetup sequenceSetup,ISetup setup)
		{
			//var setup = sequenceSetup.Setup;
			if (setup is MethodCall methodCall)
			{
				methodCall.SetCondition(
					new Condition(() =>
					{
						return Condition(sequenceSetup);
					},
					() =>
					{
						/*
							Invocation.MatchingSetup is not set until the condition has returned true. 
							If need to move the test in to the condition part
							Will need to change Setup.Matches to attach the Invocation to the Condition
						*/
						if (strict)
						{
							if (!InvocationsHaveMatchingSequenceSetup())
							{
								StrictnessFailure(UnmatchedInvocations());
							}
						}

						SetupExecuted(sequenceSetup);
					})
				);
			}
			else
			{
				throw new Exception("todo");//todo
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="unmatchedInvocations"></param>
		protected virtual void StrictnessFailure(IEnumerable<SequenceInvocation> unmatchedInvocations)
		{
			throw new StrictSequenceException { UnmatchedSequenceInvocations = unmatchedInvocations };
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sequenceSetup"></param>
		/// <returns></returns>
		protected abstract bool Condition(TSequenceSetup sequenceSetup);

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sequenceSetup"></param>
		protected virtual void SetupExecuted(TSequenceSetup sequenceSetup)
		{

		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		protected IEnumerable<SequenceInvocation> UnmatchedInvocations()
		{
			return sequenceInvocations.Where(si => !si.Matched);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		protected bool InvocationsHaveMatchingSequenceSetup()
		{
			foreach(var sequenceInvocation in UnmatchedInvocations())
			{
				if (!InvocationHasMatchingSequenceSetup(sequenceInvocation))
				{
					return false;
				}
			}
			return true;
		}

		private bool InvocationHasMatchingSequenceSetup(SequenceInvocation sequenceInvocation)
		{
			var invocation = sequenceInvocation.Invocation;
			if (invocation.MatchingSetup == null)
			{
				return false;
			}

			foreach (var setup in allSetups)
			{
				if (invocation.MatchingSetup == setup)
				{
					sequenceInvocation.Matched = true;
					break;
				}
			}
			return sequenceInvocation.Matched;
		}

		/// <summary>
		/// 
		/// </summary>
		public void Verify()
		{
			if (strict && !InvocationsHaveMatchingSequenceSetup())
			{
				throw new StrictSequenceException { UnmatchedSequenceInvocations = UnmatchedInvocations() };
			}
			VerifyImpl();
		}

		/// <summary>
		/// 
		/// </summary>
		protected abstract void VerifyImpl();
		
	}

	internal class SetupWithDepth : IEquatable<SetupWithDepth>
	{
		public int Depth { get; set; }
		public Setup Setup { get; set; }
		public SetupCollection ContainingMutableSetups { get; set; }

		public bool Equals(SetupWithDepth other)
		{
			return Setup == other.Setup;
		}

		public override int GetHashCode()
		{
			return Setup.GetHashCode();
		}
		
	}

	internal static class SetupWithDepthExtensions
	{
		public class SetupDepthComparisonResult
		{
			public bool NoChange { get; set; }
			public List<SetupWithDepth> NewSetups { get; set; }
			public SetupWithDepth TerminalSetup { get; set; }
		}

		public static SetupDepthComparisonResult NewSetups(this List<SetupWithDepth> before, List<SetupWithDepth> after)
		{
			if (after.Count == before.Count)
			{
				return new SetupDepthComparisonResult { NoChange = true };
			}

			var difference = after.Except(before, EqualityComparer<SetupWithDepth>.Default);
			var orderedByDepth = difference.OrderBy(sd => sd.Depth).ToList();
			var terminalSetup = orderedByDepth.Last();
			return new SetupDepthComparisonResult
			{
				NewSetups = orderedByDepth,
				TerminalSetup = terminalSetup
			};
		}
	}

	internal static class MoqSetupFinder
	{
		private static void GetAllSetups(SetupCollection setups, List<SetupWithDepth> setupsWithDepth, int depth)
		{
			setupsWithDepth.AddRange(setups.ToArray().Select(s => new SetupWithDepth { Depth = depth, Setup = s, ContainingMutableSetups = setups }));
			foreach (var setup in setups)
			{
				if (setup.InnerMock != null)
				{
					GetAllSetups(setup.InnerMock.MutableSetups, setupsWithDepth, depth + 1);
				}
			}
		}
		private static void GetAllSetups(SetupCollection setups, List<SetupWithDepth> setupsWithDepth)
		{
			GetAllSetups(setups, setupsWithDepth, 0);
		}
		public static List<SetupWithDepth> GetAllSetups(Mock mock)
		{
			var setupsWithDepth = new List<SetupWithDepth>();
			GetAllSetups(mock.MutableSetups, setupsWithDepth);
			return setupsWithDepth;
		}
	}

}
