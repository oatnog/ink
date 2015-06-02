﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Text;

namespace Inklewriter.Runtime
{
	public class Story : Runtime.Object
	{
        public Path currentPath { 
            get { 
                return _callStack.currentElement.path; 
            } 
            protected set {
                _callStack.currentElement.path = value;
            }
        }

        public List<Runtime.Object> outputStream;

        public Dictionary<string, Runtime.Object> variables { 
            get { 
                return _callStack.currentElement.variables; 
            } 
        }

		public List<Choice> currentChoices
		{
			get 
			{
				return CurrentOutput<Choice> ();
			}
		}

		public string currentText
		{
			get 
			{
				return string.Join(separator:"", values: CurrentOutput<Runtime.Text> ());
			}
		}

		public Story (Container rootContainer)
		{
			_rootContainer = rootContainer;

            outputStream = new List<Runtime.Object> ();

            _evaluationStack = new List<Runtime.Object> ();

            _callStack = new CallStack ();
		}

		public Runtime.Object ContentAtPath(Path path)
		{
			return _rootContainer.ContentAtPath (path);
		}

		public void Begin()
		{
			currentPath = Path.ToFirstElement ();
			Continue ();
		}

		public void Continue()
		{
			Runtime.Object currentContentObj = null;
			do {

				currentContentObj = ContentAtPath(currentPath);
				if( currentContentObj != null ) {
                					
					// Convert path to get first leaf content
					Container currentContainer = currentContentObj as Container;
					if( currentContainer != null ) {
						currentPath = currentPath.PathByAppendingPath(currentContainer.pathToFirstLeafContent);
						currentContentObj = ContentAtPath(currentPath);
					}

                    // Is the current content object:
                    //  - Normal content
                    //  - Or a logic/flow statement - if so, do it
                    bool isLogicOrFlowControl = PerformLogicAndFlowControl(currentContentObj);
                        
                    // Choice with condition?
                    bool shouldAddObject = true;
                    var choice = currentContentObj as Choice;
                    if( choice != null && choice.hasCondition ) {
                        var conditionValue = PopEvaluationStack();
                        shouldAddObject = IsTruthy(conditionValue);
                    }

                    // Content to add to evaluation stack or the output stream
                    if( !isLogicOrFlowControl && shouldAddObject ) {
                        
                        // Expression evaluation content
                        if( inExpressionEvaluation ) {
                            _evaluationStack.Add(currentContentObj);
                        }

                        // Output stream content (i.e. not expression evaluation)
                        else {
                            outputStream.Add(currentContentObj);
                        }
                    }

                    // Increment the content pointer, following diverts if necessary
					NextContent();

                    // Any push to the call stack should be done after the increment to the content pointer,
                    // so that when returning from the stack, it returns to the content after the push instruction
                    bool isStackPush = currentContentObj is ControlCommand && ((ControlCommand)currentContentObj).commandType == ControlCommand.CommandType.StackPush;
                    if( isStackPush ) {
                        _callStack.Push();
					}

				}

			} while(currentContentObj != null && currentPath != null);
		}

        // Does the expression result represented by this object evaluate to true?
        // e.g. is it a Number that's not equal to 1?
        bool IsTruthy(Runtime.Object obj)
        {
            bool truthy = false;
            if (obj is Literal) {
                var literal = (Literal)obj;
                return literal.isTruthy;
            }
            return truthy;
        }

        /// <summary>
        /// Checks whether contentObj is a control or flow object rather than a piece of content, 
        /// and performs the required command if necessary.
        /// </summary>
        /// <returns><c>true</c> if object was logic or flow control, <c>false</c> if it's normal content.</returns>
        /// <param name="contentObj">Content object.</param>
        private bool PerformLogicAndFlowControl(Runtime.Object contentObj)
        {
            if( contentObj == null ) {
                
                return false;
            }

            // Divert
            if (contentObj is Divert) {
                
                Divert currentDivert = (Divert)contentObj;
                _divertedPath = currentDivert.targetPath;
                Debug.Assert (_divertedPath != null);
                return true;
            } 

            // Branch (conditional divert)
            else if (contentObj is Branch) {
                var branch = (Branch)contentObj;
                var conditionValue = PopEvaluationStack();

                if ( IsTruthy(conditionValue) )
                    _divertedPath = branch.trueDivert.targetPath;
                else if (branch.falseDivert != null)
                    _divertedPath = branch.falseDivert.targetPath;
                
                return true;
            }

            // Start/end an expression evaluation? Or print out the result?
            else if( contentObj is ControlCommand ) {
                var evalCommand = (ControlCommand) contentObj;

                switch (evalCommand.commandType) {

                case ControlCommand.CommandType.EvalStart:
                    Debug.Assert (inExpressionEvaluation == false, "Already in expression evaluation?");
                    inExpressionEvaluation = true;
                    break;

                case ControlCommand.CommandType.EvalEnd:
                    Debug.Assert (inExpressionEvaluation == true, "Not in expression evaluation mode");
                    inExpressionEvaluation = false;
                    break;

                case ControlCommand.CommandType.EvalOutput:

                    // If the expression turned out to be empty, there may not be anything on the stack
                    if (_evaluationStack.Count > 0) {
                        
                        var output = PopEvaluationStack ();

                        // Functions may evaluate to Void, in which case we skip output
                        if (!(output is Void)) {
                            // TODO: Should we really always blanket convert to string?
                            // It would be okay to have numbers in the output stream the
                            // only problem is when exporting text for viewing, it skips over numbers etc.
                            var text = new Text (output.ToString ());

                            outputStream.Add (text);
                        }

                    }
                    break;

                // Actual stack push/pop will be performed after Step in main loop
                case ControlCommand.CommandType.StackPush:
                    break;

                case ControlCommand.CommandType.StackPop:
                    _callStack.Pop();
                    break;
                }

                return true;
            }

            // Variable assignment
            else if( contentObj is VariableAssignment ) {
                var varAss = (VariableAssignment) contentObj;
                var assignedVal = PopEvaluationStack();
                variables[varAss.variableName] = assignedVal;

                return true;
            }

            // Variable reference
            else if( contentObj is VariableReference ) {
                var varRef = (VariableReference)contentObj;
                var varContents = _callStack.GetVariableWithName (varRef.name);
                if (varContents == null) {
                    
                    throw new System.Exception ("Uninitialised variable: " + varRef.name);
                }
                _evaluationStack.Add( varContents );
                return true;
            }

            // Native function call
            else if( contentObj is NativeFunctionCall ) {
                var func = (NativeFunctionCall) contentObj;
                var funcParams = PopEvaluationStack(func.numberOfParameters);
                var result = func.Call(funcParams);
                _evaluationStack.Add(result);
                return true;
            }

            // No control content, must be ordinary content
            return false;
        }

		public void ContinueFromPath(Path path)
		{
			currentPath = path;
			Continue ();
		}

		public void ContinueWithChoiceIndex(int choiceIdx)
		{
			var choices = this.currentChoices;
			Debug.Assert (choiceIdx >= 0 && choiceIdx < choices.Count);

			var choice = choices [choiceIdx];

			outputStream.Add (new ChosenChoice (choice));

			ContinueFromPath (choice.pathOnChoice);
		}

        protected Runtime.Object PopEvaluationStack()
        {
            var obj = _evaluationStack.Last ();
            _evaluationStack.RemoveAt (_evaluationStack.Count - 1);
            return obj;
        }

        protected List<Runtime.Object> PopEvaluationStack(int numberOfObjects)
        {
            
            var popped = _evaluationStack.GetRange (_evaluationStack.Count - numberOfObjects, numberOfObjects);
            _evaluationStack.RemoveRange (_evaluationStack.Count - numberOfObjects, numberOfObjects);
            return popped;
        }
			
		public List<T> CurrentOutput<T>() where T : class
		{
			List<T> result = new List<T> ();

			for (int i = outputStream.Count - 1; i >= 0; --i) {
				object outputObj = outputStream [i];

				// "Current" is defined as "since last chosen choice"
				if (outputObj is ChosenChoice) {
					break;
				}

				T outputOfType = outputObj as T;
				if (outputOfType != null) {

					// Insert rather than Add since we're iterating in reverse
					result.Insert (0, outputOfType);
				}
			}

			return result;
		}

        public virtual string BuildStringOfHierarchy()
        {
            var sb = new StringBuilder ();


            Runtime.Object currentObj = null;
            if (currentPath != null) {
                currentObj = ContentAtPath (currentPath);
            }
            _rootContainer.BuildStringOfHierarchy (sb, 0, currentObj);

            return sb.ToString ();
        }

		private void NextContent()
		{
			// Divert step?
			if (_divertedPath != null) {
				currentPath = _divertedPath;
				_divertedPath = null;

                // Diverted location has valid content?
                if (ContentAtPath (currentPath) != null) {
                    return;
                }
				
                // Otherwise, if diverted location doesn't have valid content,
                // drop down and attempt to increment.
                // This can happen if the diverted path is intentionally jumping
                // to the end of a container - e.g. a Conditional that's re-joining
			}

			// Can we increment successfully?
			currentPath = _rootContainer.IncrementPath (currentPath);
			if (currentPath == null) {

				// Failed to increment, so we've run out of content
				// Try to pop call stack if possible
				if ( _callStack.canPop ) {

					// Pop from the call stack
                    _callStack.Pop();

                    // This pop was due to dropping off the end of a function that didn't return anything,
                    // so in this case, we make sure that the evaluator has something to chomp on if it needs it
                    if (inExpressionEvaluation) {
                        _evaluationStack.Add (new Runtime.Void());
                    }

					// Step past the point where we last called out
					NextContent ();
				}
			}
		}

        private Container _rootContainer;
        private Path _divertedPath;
            
        private CallStack _callStack;

        private List<Runtime.Object> _evaluationStack;

        private bool inExpressionEvaluation {
            get {
                return _callStack.currentElement.inExpressionEvaluation;
            }
            set {
                _callStack.currentElement.inExpressionEvaluation = value;
            }
        }
	}
}

