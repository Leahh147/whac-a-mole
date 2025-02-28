using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using WhacAMole.Scripts.Audio;

namespace UserInTheBox
{
    public class RLEnv_Whacamole : RLEnv
    {
        // This class implements the RL environment for the Whacamole game.

        public SequenceManager sequenceManager;
        // public AudioManager audioManager;
        [SerializeField] private Func<float, float> _distRewardFunc;
        [SerializeField] private bool _useRewardSplines=false;
        private float _previousPoints, _initialPoints, _previousContacts, _initialContacts, _elapsedTimeScaled;
        private Transform _marker;
        private string _condition;
        private bool _denseGameReward;
        private int _fixedSeed;
        private bool _debug;
        private List<String> _usedConditions;
        private int _conditionIndex;
        
        public override void InitialiseReward()
        {
            _initialPoints = sequenceManager.Points;
            _previousPoints = _initialPoints;
            _initialContacts = sequenceManager.Contacts;
            _previousContacts = _initialContacts;
            _marker = simulatedUser.rightHandController.Find("Hammer/marker").transform;

            if (_useRewardSplines)
            {
                var _xmax = 0.8f;  //*100  #initial/maximum distance that can be typically reached; used to scale entire reward term relative to other rewards
                var _ymax = -0.1f;  //minimum negative reward; used to scale entire reward term relative to other rewards
                // var _xeps = _playParameters.targetSize[1];
                var _xeps = 0.025f;  //*100  #"sufficient" distance to fulfill the pointing task (typically, this corresponds to the target radius); often, if dist<=_xeps (for the first time), an additional bonus reward is given
                var _yeps = -0.0f;  //-0.05  #reward given at target boundary (WARNING: needs to be non-positive!); should be chosen between 10%*_ymax and _ymin=0
                var _xref = 0.3f;  //*100  #"expected" distance; used to scale gradients of this reward term appropriately
                // we set _positive_only to true, as otherwise the maximum distance reward of zero is also given whenever a target disappears but the next ones are not available yet (happens quite often in the easy condition with only one target at once)
                var _positive_only = true;  //whether to ensure that all reward terms are non-negative (WARNING: non-negative values only guaranteed if initial distance _xmax is the maximum reachable distance!)

                var curve = new HermiteCurve();
                curve.Initialise(maxDist:_xmax, minReward:_ymax, epsDist:_xeps, epsReward:_yeps, refDist:_xref, positiveOnly:_positive_only);
                _distRewardFunc = curve.Evaluate;
            }
            else
            {
                _distRewardFunc = dist => (float)(Math.Exp(-10*dist)-1) / 10;
            }
            
        }

        public override void InitialiseGame()
        {
            // Check if debug mode is enabled
            _debug = Application.isEditor;  //UitBUtils.GetOptionalArgument("debug");

            // Get game variant and level
            if (!_debug)
            {
                _condition = UitBUtils.GetKeywordArgument("condition");
                _logging = UitBUtils.GetOptionalArgument("logging");
                sequenceManager.adaptiveTargetSpawns = UitBUtils.GetOptionalArgument("adaptive");
                _denseGameReward = !UitBUtils.GetOptionalArgument("sparse");
                simulatedUser.audioModeOn = UitBUtils.GetOptionalArgument("audioModeOn");
                if (simulatedUser.audioModeOn) { // needs changes!!somehow the optional keywords are not imported
                    string signalType_ = UitBUtils.GetOptionalKeywordArgument("signalType", "Mono");
                    string sampleType_ = UitBUtils.GetOptionalKeywordArgument("sampleType", "Amplitude");
                    audioManager.SignalType = signalType_;
                    audioManager.SampleType = sampleType_;
                    Debug.Log("Audio mode on, using signal type " + audioManager.SignalType + " and sample type " + audioManager.SampleType);
                }

                string fixedSeed = UitBUtils.GetOptionalKeywordArgument("fixedSeed", "0");
                // Try to parse given fixed seed string to int
                if (!Int32.TryParse(fixedSeed, out _fixedSeed))
                {
                    Debug.Log("Couldn't parse fixed seed from given value, using default 0");
                    _fixedSeed = 0;
                }

            }
            else
            {
                _condition = "easy";  // set to easy for debugging;
                _denseGameReward = true;
                _fixedSeed = 0;
                _logging = false;
                simulatedUser.audioModeOn = true;
                if (simulatedUser.audioModeOn) {
                    audioManager.SignalType = "Mono";
                    audioManager.SampleType = "Spectrum";
                    Debug.Log("Audio mode on, using signal type " + audioManager.SignalType + " and sample type " + audioManager.SampleType);
                }
            }
            Debug.Log("RLEnv set to condition " + _condition);

        }

        public override void UpdateIsFinished()
        {
            // Add additional logs
            for (int i = 0; i < sequenceManager.targetArea.numberGridPosition; i++)
            {
                _logDict[string.Format("failrateTarget{0}", i)] = (float)sequenceManager.MissesGridID[i] / ((sequenceManager.PunchesGridID[i] + sequenceManager.MissesGridID[i]) > 0 ? (sequenceManager.PunchesGridID[i] + sequenceManager.MissesGridID[i]) : 1);
            }

            // Update finished
            _isFinished = sequenceManager.stateMachine.currentState.Equals(GameState.Ready);
        }

        protected override void CalculateReward()
        {
            // Get hit points
            int points = sequenceManager.Points;
            _logDict["Points"] = points;
            _reward = (points - _previousPoints)*10;
            // _logDict["RewardGame"] = _reward;
            _previousPoints = points;

            if (_denseGameReward)
            {
                // Get points for first unsuccessful contacts as well
                int contacts = sequenceManager.FirstContacts;
                float contactVelocity = sequenceManager.lastContactVelocity;
                contactVelocity = contactVelocity > 0 ? contactVelocity : 0;
                float _unsuccessfulContactReward = (contacts - _previousContacts)*2*(contactVelocity/0.8f);
                _logDict["RewardUnsuccessfulContact"] = _unsuccessfulContactReward;
                _reward += _unsuccessfulContactReward;
                _previousContacts = contacts;
                
                // Also calculate distance component
                float _distanceReward = 0f;
                foreach (var target in sequenceManager.targetArea.GetComponentsInChildren<Target>())
                {
                    if (target.stateMachine.currentState == TargetState.Alive)
                    {
                        var dist = Vector3.Distance(target.transform.position, _marker.position);
                        // _distanceReward += (float)(Math.Exp(-10*dist)-1) / 10;
                        _distanceReward += _distRewardFunc(dist);

                        // Console.WriteLine("dist: " + dist);
                        // Console.WriteLine("reward: " + _distanceReward);
                    }
                }
                _logDict["DistanceReward"] = _distanceReward;
                _reward += _distanceReward;
            }
        }

        public override float GetTimeFeature()
        {
            return sequenceManager.GetTimeFeature();
        }


        public override void Reset()
        {            
            // Set play level
            if (!_condition.StartsWith("random"))
            {
                sequenceManager.playParameters.condition = _condition;
            }
            else
            {
                // Randomly select condition
                if (_condition == "random-easy")
                {
                    _usedConditions = new List<String> { "low-easy", "easy", "high-easy" };
                    _conditionIndex = Random.Range(0, _usedConditions.Count);
                }
                else if (_condition == "random-easy-medium")
                {
                    _usedConditions = new List<String> { "low-easy", "easy", "high-easy", "low-medium", "medium", "high-medium" };
                    _conditionIndex = Random.Range(0, _usedConditions.Count);
                }
                else if (_condition == "random-unconstrained")
                {
                    _usedConditions = new List<String> { "low-medium-unconstrained", "medium-unconstrained", "high-medium-unconstrained" };
                    _conditionIndex = Random.Range(0, _usedConditions.Count);
                }
                else
                {
                    _usedConditions = new List<String> { "low-medium", "medium", "high-medium" };
                    _conditionIndex = Random.Range(0, _usedConditions.Count);
                }
                sequenceManager.playParameters.condition = _usedConditions[_conditionIndex];
            }
            sequenceManager.playParameters.Initialise(_fixedSeed);
            
                
            // Override headset position of simulated user, if necessary
            if (simulatedUser.enabled)
            {
                overrideHeadsetOrientation = true;
                
                if (sequenceManager.playParameters.condition.StartsWith("low"))
                {
                    simulatedUserHeadsetOrientation = new Quaternion(-0.258819f, 0f, 0f, -0.9659258f);
                }
                else if (sequenceManager.playParameters.condition.StartsWith("high"))
                {
                    simulatedUserHeadsetOrientation = new Quaternion(0.258819f, 0f, 0f, -0.9659258f);
                }
                else
                {
                    simulatedUserHeadsetOrientation = new Quaternion(-0.0871556774f, 0f, 0f, -0.99619472f);
                }
            }
            
            // Visit Ready state, as some important stuff will be set (on exit)
            sequenceManager.stateMachine.GotoState(GameState.Ready);

            // Start playing
            sequenceManager.stateMachine.GotoState(GameState.Play);
            
            // Reset points
            _previousPoints = sequenceManager.Points;
        }
    }
}