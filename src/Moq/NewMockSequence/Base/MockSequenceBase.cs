﻿// Copyright (c) 2007, Clarius Consulting, Manas Technology Solutions, InSTEDD, and Contributors.
// All rights reserved. Licensed under the BSD 3-Clause License; see License.txt.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Moq
{

	/// <summary>
	/// 
	/// </summary>     
	public abstract class MockSequenceBase<TSequenceSetup, TInvocationShapeSetups> 
		where TSequenceSetup : SequenceSetupBase 
		where  TInvocationShapeSetups : InvocationShapeSetupsBase<TSequenceSetup>
	{
		private int setupCount = -1;
		private readonly Mock[] mocks;
		private readonly SequenceInvocationListener sequenceInvocationListener;
		private readonly List<ISetup> allSetups = new List<ISetup>();
		private readonly List< TInvocationShapeSetups> allInvocationShapeSetups = new List< TInvocationShapeSetups>();
		private readonly List<TSequenceSetup> sequenceSetups = new List<TSequenceSetup>();
		/// <summary>
		/// 
		/// </summary>
		protected readonly bool strict;
		/// <summary>
		/// 
		/// </summary>
		protected IReadOnlyList<TSequenceSetup> SequenceSetups => sequenceSetups;

		internal List<SequenceInvocation> SequenceInvocations => sequenceInvocationListener.SequenceInvocations;
		internal IReadOnlyList<ISetup> AllSetups => allSetups;
		
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
			sequenceInvocationListener = new SequenceInvocationListener(mocks);
			sequenceInvocationListener.ListenForInvocations();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="setup"></param>
		/// <param name="sequenceSetupCallback"></param>
		protected void InterceptSetup(Action setup, Action<TSequenceSetup> sequenceSetupCallback)
		{
			setupCount++;
			List<List<SetupWithDepth>> allSetupsBefore = mocks.Select(m => SetupFinder.GetAllSetups(m)).ToList();
			setup();
			List<List<SetupWithDepth>> allSetupsAfter = mocks.Select(m => SetupFinder.GetAllSetups(m)).ToList();
			for(var i = 0; i < allSetupsBefore.Count; i++)
			{
				var result = allSetupsBefore[i].NewSetups(allSetupsAfter[i]);
				if (!result.NoChange)
				{
					allSetups.AddRange(result.NewSetups.Select(sd => sd.Setup));
					sequenceInvocationListener.ListenForInvocations(result.NewSetups.Select(s => s.Setup.Mock));
					var terminalSetup = result.TerminalSetup.Setup;

					var sequenceSetup = CreateSequenceSetup(terminalSetup);
					InitializeSequenceSetup(sequenceSetup);
					sequenceSetupCallback(sequenceSetup);

					return;
				}

			}

			throw new ArgumentException("No setup performed",nameof(setup));
		}
		
		private void InitializeSequenceSetup(TSequenceSetup sequenceSetup)
		{
			sequenceSetups.Add(sequenceSetup);
			ApplyInvocationShapeSetups(sequenceSetup);
			SetCondition(sequenceSetup, sequenceSetup.SetupInternal);

		}
		
		private TSequenceSetup CreateSequenceSetup(Setup setup)
		{
			var sequenceSetup = (TSequenceSetup)Activator.CreateInstance(typeof(TSequenceSetup));
			sequenceSetup.SetupIndex = setupCount;
			sequenceSetup.SetupInternal = setup;
			
			return sequenceSetup;
		}

		private void ApplyInvocationShapeSetups(TSequenceSetup newSequenceSetup)
		{
			var invocationShape = newSequenceSetup.SetupInternal.Expectation;
			var invocationShapeSetups = allInvocationShapeSetups.SingleOrDefault(ts => ts.InvocationShape.Equals(invocationShape));
			if (invocationShapeSetups == null)
			{
				invocationShapeSetups = (TInvocationShapeSetups) Activator.CreateInstance(typeof( TInvocationShapeSetups),newSequenceSetup);
				allInvocationShapeSetups.Add(invocationShapeSetups);
			}
			else
			{
				invocationShapeSetups.AddSequenceSetup(newSequenceSetup);
			}

		}

		private void SetCondition(TSequenceSetup sequenceSetup,ISetup setup)
		{
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
		protected virtual void StrictnessFailure(IEnumerable<ISequenceInvocation> unmatchedInvocations)
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
		protected IEnumerable<ISequenceInvocation> UnmatchedInvocations()
		{
			return UnmatchedInvocationsInternal().Cast<ISequenceInvocation>();
		}

		private IEnumerable<SequenceInvocation> UnmatchedInvocationsInternal()
		{
			return SequenceInvocations.Where(si => !si.Matched);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		protected bool InvocationsHaveMatchingSequenceSetup()
		{
			foreach(var sequenceInvocation in UnmatchedInvocationsInternal())
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

}
