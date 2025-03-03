using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UserInTheBox;

public enum GameState {
  Startup         = 0,
  Ready           = 1,
  Countdown       = 2,
  Play            = 3,
}

public class SequenceManager : MonoBehaviour {
  
  // State machine
  public StateMachine<GameState> stateMachine;

  // Headset transform for setting game height
  public Transform headset;
  
  // Right controller for visualising controller ray / hammer
  public GameObject rightController;
  private XRInteractorLineVisual _lineVisual;
  private GameObject _hammer;
  
  // Marker from the hammer
  private Transform _marker;
  
  // Set some info texts
  public Transform scoreBoard;
  public TextMeshPro pointCounterText;
  public TextMeshPro roundCounterText;
  private bool _showScoreboard = false;
  public event System.Action PlayStop;
  
  // UI for choosing game and level etc
  public GameObject startMenu;
  public GameObject orderChooser;
  public GameObject pauseMenu;
  public GameObject endText;

  // Game State (for all types of states)
  private RunIdentification currentRunId;
  
  // Needed for countdown
  private float _countdownStart;
  private const float CountdownDuration = 4;
  public TextMeshPro countdownText;
  
  // Keep score of points
  private int _points;
  public int Points
  {
    get => _points;
    private set
    {
      _points = value;
    }
  }
  
  // Keep track of hits, misses, and contacts (hits with insufficient velocity)
  private int _punches;
  private int _misses;
  private int _contacts;
  public int Contacts
  {
    get => _contacts;
    private set
    {
      _contacts = value;
    }
  }
  private int _firstContacts;
  public int FirstContacts
  {
    get => _firstContacts;
    private set
    {
      _firstContacts = value;
    }
  }
  private List<int> _punches_gridID;
  public List<int> PunchesGridID
  {
    get => _punches_gridID;
    private set
    {
      _punches_gridID = value;
    }
  }
  private List<int> _misses_gridID;
  public List<int> MissesGridID
  {
    get => _misses_gridID;
    private set
    {
      _misses_gridID = value;
    }
  }
  private List<int> _contacts_gridID;
  public List<int> ContactsGridID
  {
    get => _contacts_gridID;
    private set
    {
      _contacts_gridID = value;
    }
  }
  private List<int> _contactlessTargets = new List<int>();
  public bool adaptiveTargetSpawns = false;
  private float _uniformProb = 0.5f;
  private List<float> _spawnProbs_gridID;

  public float lastContactVelocity = 0f;
  public float lastHitVelocity = 0f;
  
  // Start time
  private float _roundStart;
  
  // Boolean to indicate whether episode should be terminated
  private bool _terminate;
  
  // Play parameters
  public PlayParameters playParameters;
  
  // Logger
  public UserInTheBox.Logger logger;
  private bool _logger_initialised = false;

  // Recorder
  public Recorder recorder;
  
  // Target area where targets are spawned
  public TargetArea targetArea;

  public AudioManager audioManager;  
  // State getters
  public RunIdentification CurrentRunIdentification { get {return currentRunId; }}
  
  // Define condition orders
  public Dictionary<string, List<string>> conditionOrders = new Dictionary<string, List<string>>
  {
    { "Alpha",    new List<string>{"easy", "medium", "hard", "low", "mid", "high"} },
    { "Bravo",    new List<string>{"easy", "hard", "medium", "low", "high", "mid"} },
    { "Charlie",  new List<string>{"medium", "easy", "hard", "mid", "low", "high"} },
    { "Delta",    new List<string>{"medium", "hard", "easy", "mid", "high", "low"} },
    { "Echo",     new List<string>{"hard", "easy", "medium", "high", "low", "mid"} },
    { "Foxtrot",  new List<string>{"hard", "medium", "easy", "high", "mid", "low"} },
  };
  private string _experimentOrder;
  private int _conditionIdx;
  private bool _isExperiment = false;

  // Run State
  void InitStateMachine() {
    stateMachine  = new StateMachine<GameState>(GameState.Startup);
    
    // State transitions
    stateMachine.AddTransition(GameState.Startup,         GameState.Ready);
    stateMachine.AddTransition(GameState.Ready,           GameState.Countdown);
    stateMachine.AddTransition(GameState.Countdown,       GameState.Play);
    
    // On Enter
    stateMachine.State(GameState.Startup)                 .OnEnter += () => InitRun();
    stateMachine.State(GameState.Ready)                   .OnEnter += () => OnEnterReady();
    stateMachine.State(GameState.Countdown)               .OnEnter += () => OnEnterCountdown();
    stateMachine.State(GameState.Play)                    .OnEnter += () => OnEnterPlay();

    // On Update
    stateMachine.State(GameState.Startup)                 .OnUpdate += () => stateMachine.GotoNextState(); // Allow one update so headset position is updated
    stateMachine.State(GameState.Countdown)               .OnUpdate += () => OnUpdateCountdown();
    stateMachine.State(GameState.Play)                    .OnUpdate += () => OnUpdatePlay();
    
    // On Exit
    stateMachine.State(GameState.Ready)                   .OnExit += () => OnExitReady();
    stateMachine.State(GameState.Play)                    .OnExit += () => OnExitPlay();
  }

  void Awake()
  {
    // Set target frame rate to 60 Hz. This will be overwritten by SimulatedUser during simulations
    Application.targetFrameRate = 60;
    
    // Get ray line renderer and hammer renderer
    _lineVisual = rightController.GetComponent<XRInteractorLineVisual>();
    _hammer = rightController.GetNamedChild("Hammer");
    
    // Get the marker from the hammer
    _marker = _hammer.GetNamedChild("marker").transform;

    // Ray line is enabled by default, as is hammer
    _lineVisual.enabled = true;
    _hammer.SetActive(true);
    
    // Initialise play parameters
    playParameters = new PlayParameters();

    // Don't show countdown text or scoreboard
    countdownText.enabled = false;
    scoreBoard.gameObject.SetActive(false);

    // Initialise state machine
    InitStateMachine();
    stateMachine.State(stateMachine.currentState).InvokeOnEnter();

  }
  
  void FixedUpdate()
  {
    stateMachine.CurrentState().InvokeOnFixedUpdate();
  }
  
  void Update() {
    stateMachine.CurrentState().InvokeOnUpdate();

    // Adjust audio volume based on distance to the nearest target
    if (targetArea.objects?.Count > 0)
    {
        float minDistance = float.MaxValue;
        foreach (var target in targetArea.objects.Values)
        {
            float distance = Vector3.Distance(_marker.position, target.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
            }
        }
        if (Globals.Instance.simulatedUser.audioModeOn) {
          audioManager.m_AudioSource.volume = Mathf.Clamp(1 / (minDistance + 1), 0, 1); // Adding 1 to avoid division by zero
        }
    }
  }

  public void RecordSpawn(Target target) 
  {
    _contactlessTargets.Add(target.ID);
    if (logger.enabled)
    {
      logger.PushWithTimestamp("events", "target_spawn, target ID " + target.ID +
                                         ", grid ID " + target.GridID);
    }
  }

  public void RecordHit(Target target, Vector3 velocity) 
  {
    Points = _points + target.points;
    _punches += 1;
    _punches_gridID[target.GridID] += 1;
    _contactlessTargets.Remove(target.ID);
    if (logger.enabled)
    {
      logger.PushWithTimestamp("events", "target_hit, target ID " + target.ID +
                                         ", grid ID " + target.GridID + 
                                         ", velocity " + UitBUtils.Vector3ToString(velocity, " "));
    }
    if (playParameters.condition.StartsWith("low"))
    {
      lastHitVelocity = -velocity.y;  //use downward velocity for low conditions, as used for playParameter.velocityThreshold in this case
    }
    else {
      lastHitVelocity = velocity.z;  //use forward velocity for all other conditions
    }

    // Adjust audio volume based on distance to target
    float distance = Vector3.Distance(_marker.position, target.transform.position);
    if (Globals.Instance.simulatedUser.audioModeOn) {
      audioManager.m_AudioSource.volume = Mathf.Clamp(1 / (distance + 1), 0, 1); // Adding 1 to avoid division by zero
    }
  }

  public void RecordContact(Target target, Vector3 velocity)
  {
    _contacts += 1;
    _contacts_gridID[target.GridID] += 1;
    if (_contactlessTargets.Contains(target.ID))
    {
      _firstContacts += 1;
      _contactlessTargets.Remove(target.ID);
    }

    if (logger.enabled)
    {
      logger.PushWithTimestamp("events", "target_contact, target ID " + target.ID +
                                         ", grid ID " + target.GridID +
                                         ", velocity " + UitBUtils.Vector3ToString(velocity, " "));
    }
    if (playParameters.condition.StartsWith("low"))
    {
      lastContactVelocity = -velocity.y;  //use downward velocity for low conditions, as used for playParameter.velocityThreshold in this case
    }
    else {
      lastContactVelocity = velocity.z;  //use forward velocity for all other conditions
    }

    // Adjust audio volume based on distance to target
    float distance = Vector3.Distance(_marker.position, target.transform.position);
    if (Globals.Instance.simulatedUser.audioModeOn) {
      audioManager.m_AudioSource.volume = Mathf.Clamp(1 / (distance + 1), 0, 1); // Adding 1 to avoid division by zero
    }
  }
  
  public void RecordMiss(Target target)
  {
    _misses += 1;

    _misses_gridID[target.GridID] += 1;
    _contactlessTargets.Remove(target.ID);
    if (logger.enabled)
    {
      logger.PushWithTimestamp("events", "target_miss, target ID " + target.ID +
                                         ", grid ID " + target.GridID);
    }
  }
  
  public void RecordBombDetonation(Bomb bomb)
  {
    Points = _points + bomb.points;
    if (logger.enabled)
    {
      logger.PushWithTimestamp("events", "bomb_detonate, " + bomb.ID + ", " + bomb.PositionToString());
    }
    // _terminate = true;
    // targetArea.RemoveBomb(bomb);
  }

  public void RecordBombDisarm(Bomb bomb)
  {
    if (logger.enabled)
    {
      logger.PushWithTimestamp("events", "bomb_disarm, " + bomb.ID + ", " + bomb.PositionToString());
    }
    // targetArea.RemoveBomb(bomb);
  }

  public void UpdateExperimentOrder(string order)
  {
    _experimentOrder = order;
  }

  public void InitExperiment()
  {
    _conditionIdx = 0;
    _isExperiment = true;
    playParameters.condition = conditionOrders[_experimentOrder][_conditionIdx];
  }
  
  void InitRun() 
  {
    // I don't think these are used for anything
    var uid = System.Guid.NewGuid().ToString();
    currentRunId = new RunIdentification();
    currentRunId.uuid = uid;
    currentRunId.startWallTime = System.DateTime.Now.ToString(Globals.Instance.timeFormat);
    currentRunId.startRealTime = Time.realtimeSinceStartup;
    
    // Hide target area
    targetArea.gameObject.SetActive(false);
    
    // Show game and level choosers
    startMenu.SetActive(true);
    orderChooser.SetActive(true);
    pauseMenu.SetActive(false);
    endText.SetActive(false);
  }
  
  void OnEnterPlay() 
  {
    // Increment round, start points from zero
    Points = 0;
    _punches = 0;
    _misses = 0;
    _contacts = 0;
    _firstContacts = 0;
    if (_punches_gridID is null)
    {
      _punches_gridID = Enumerable.Repeat(0, targetArea.numberGridPosition).ToList();      
    }
    if (_misses_gridID is null)
    {
      _misses_gridID = Enumerable.Repeat(0, targetArea.numberGridPosition).ToList();
    }
    if (_contacts_gridID is null)
    {
      _contacts_gridID = Enumerable.Repeat(0, targetArea.numberGridPosition).ToList();
    }
    _roundStart = Time.time;
    _terminate = false;
    
    // Reset target id counter
    targetArea.Reset();
    
    // Set target probabilities
    if (adaptiveTargetSpawns)  //TODO: move this code to TargetArea.cs
    {
      // // Store target hit rates from last run
      // _hitratio_gridID = new List<float>();
      // for (int i = 0; i < _contacts_gridID.Count; i++)
      // {
      //   _hitratio_gridID[i] = (float)_punches_gridID[i] / (_punches_gridID[i] + _misses_gridID[i]);
      // }
      
      // Compute target spawn probabilities for all grid IDs based on fail rates from last run
      _spawnProbs_gridID = Enumerable.Repeat(0.0f, targetArea.numberGridPosition).ToList();
      for (int i = 0; i < _spawnProbs_gridID.Count; i++)
      {
        _spawnProbs_gridID[i] = (float)_misses_gridID[i] / ((_punches_gridID[i] + _misses_gridID[i]) > 0 ? (_punches_gridID[i] + _misses_gridID[i]) : 1);
      }
      // Add constant "base probability" to each target
      float pt = _spawnProbs_gridID.Sum();
      int pn = _contacts_gridID.Count;
      for (int i = 0; i < pn; i++)
      {
        _spawnProbs_gridID[i] += (_uniformProb / (1.0f - _uniformProb)) * ((pt / pn) > 0 ? (pt / pn) : 1);
      }
    }
    
    // Reset target event counters (needs to be done after spawn probabilities are calculated)
    _punches_gridID = Enumerable.Repeat(0, targetArea.numberGridPosition).ToList();
    _misses_gridID = Enumerable.Repeat(0, targetArea.numberGridPosition).ToList();
    _contacts_gridID = Enumerable.Repeat(0, targetArea.numberGridPosition).ToList();

    // Show scoreboard
    if (_showScoreboard)
    {
      scoreBoard.gameObject.SetActive(true);
      
      // Update scoreboard
      UpdateScoreboard();
    }
  }

  void UpdateScoreboard()
  {
    // Update timer
    float elapsed = Time.time - _roundStart;
    roundCounterText.text = (elapsed >= playParameters.roundLength ? 0 : playParameters.roundLength - elapsed).ToString("N1");

    // Update point counter text
    pointCounterText.text = _punches.ToString("N0") + " / " + _misses.ToString("N0");
  }
  
  void OnUpdatePlay() 
  {
    // Update scoreboard (timer and point / hit rate counter)
    if (_showScoreboard)
    {
      UpdateScoreboard();
    }

    if (logger.enabled)
    {
      // Log position
      logger.PushWithTimestamp("states", GetStateString());
    }

    // Check if time is up for this trial; or if we should terminate for other reasons
    if ((Time.time - _roundStart > playParameters.roundLength) || _terminate)
    {
        stateMachine.GotoState(GameState.Ready);
    }
    else
    {
      // Continue play; potentially spawn a new target and/or a new bomb
      if (adaptiveTargetSpawns)
      {
        targetArea.MaybeSpawnTarget(_spawnProbs_gridID);
      }
      else
      {
        targetArea.MaybeSpawnTarget();
      }
    }
  }

  void OnExitPlay()
  {
    // Stop playing
    if (PlayStop != null) PlayStop();

    // Hide target area
    targetArea.gameObject.SetActive(false);
    
    // Hide scoreboard
    scoreBoard.gameObject.SetActive(false);
    
    if (logger.enabled)
    {
      // Log stats
      logger.PushWithTimestamp("events", "episode statistics, contacts " + _contacts + ", hits " + _punches + 
                                         ", misses " + _misses + ", hit rate " + (float)_punches / (_punches + _misses));
      
      // Stop logging
      logger.Finalise("states");
      logger.Finalise("events");
    }
    
    // Increment condition counter
    if (_isExperiment)
    {
      _conditionIdx += 1;
    }
  }

  void OnEnterReady() 
  {    
    // Hide hammer, show ray
    _lineVisual.enabled = true;

    // When running an experiment, behaviour of GameState.Ready depends on whether there are conditions left
    if (_isExperiment)
    {
      if (_conditionIdx < conditionOrders[_experimentOrder].Count)
      {
        playParameters.condition = conditionOrders[_experimentOrder][_conditionIdx];
        // Show pause text and button
        pauseMenu.SetActive(true);
      }
      else
      {
        // Show game end text
        endText.SetActive(true);
      }
    }
    else
    {
      // Show initial menu
      startMenu.SetActive(true);
      orderChooser.SetActive(true);
    }
  }
  
  void OnExitReady() 
  {
    
    // Initialise play
    playParameters.Initialise();
    
    // Display target area
    targetArea.gameObject.SetActive(true);
    
    // Play parameters have been chosen, update them
    targetArea.SetPlayParameters(playParameters);
    
    // Set target area to correct position
    targetArea.SetPosition(headset);

    // Calculate grid mapping (for logging)
    targetArea.CalculateGridMapping();
    
    // Scale target area correctly
    targetArea.SetScale();

    if (_showScoreboard)
    {
      // Set also position of point screen (showing timer/score)
      Vector3 scoreBoardOffset = new Vector3(-playParameters.targetAreaWidth, 0.0f, 0.0f);
      scoreBoard.SetPositionAndRotation(targetArea.transform.position + scoreBoardOffset, Quaternion.identity);
    }
    
    // Hide the controller ray, show hammer
    _lineVisual.enabled = false;
    
    // Hide all menus
    startMenu.SetActive(false);
    orderChooser.SetActive(false);
    pauseMenu.SetActive(false);
    
    // Initialise log files
    if (logger.enabled)
    {
      if (!_logger_initialised)
      {
        // Create folder for this experiment
        logger.GenerateExperimentFolder(playParameters.condition);
        
        logger.Initialise("states");
        logger.Initialise("events");
        _logger_initialised = true;
      }
      
      // Write level and random seed
      logger.Push("states", "condition " + playParameters.condition + 
                            ", random seed " + playParameters.randomSeed);
      
      // Write headers
      logger.Push("states", GetStateHeader());
      
      // Do first logging here, so we get correctly positioned controllers/headset when game begins
      logger.PushWithTimestamp("states", GetStateString());
      
      // Add target plane transform
      logger.PushWithTimestamp("events", "target plane transform " + 
                                         UitBUtils.TransformToString(targetArea.transform, " "));
      
      // Add grid id-ji-xy mapping
      logger.PushWithTimestamp("events", targetArea.GetGridMapping());

    }

  }

  public string GetStateHeader()
  {
    return UitBUtils.GetStateHeader() +
           ", marker_pos_x, marker_pos_y, marker_pos_z, marker_quat_x, marker_quat_y, marker_quat_z, marker_quat_w";
  }
  
  string GetStateString()
  {
    return UitBUtils.GetStateString(Globals.Instance.simulatedUser) + ", " + UitBUtils.TransformToString(_marker);
  }
  
  void OnEnterCountdown()
  {
    _countdownStart = Time.time;
    countdownText.text = (CountdownDuration-1).ToString();
    countdownText.enabled = true;
  }
  void OnUpdateCountdown()
  {
    float elapsed = CountdownDuration - (Time.time - _countdownStart);
    countdownText.text = Math.Max(elapsed, 1).ToString("N0");

    // Go to Play state after countdown reaches zero
    if (elapsed <= 0)
    {
      countdownText.enabled = false;
      stateMachine.GotoNextState();
    }
    // Hide after one
    else if (elapsed <= 1 && countdownText.text != "Go!")
    {
      countdownText.text = "Go!";
    }
  }

  public void SetCondition(string condition, int randomSeed)
  {
    playParameters.condition = condition;
    playParameters.randomSeed = randomSeed;
    
    // Visit Ready state, as some important stuff will be set (on exit)
    stateMachine.GotoState(GameState.Ready);
    
    // Start playing
    stateMachine.GotoState(GameState.Play);
  }

  public float GetTimeFeature()
  {
    // Calculate how much time has elapsed in this round (scaled [-1, 1])
    float _elapsedTimeScaled = 2*((Time.time - _roundStart) / playParameters.roundLength) - 1;
    return _elapsedTimeScaled;
  }
}
