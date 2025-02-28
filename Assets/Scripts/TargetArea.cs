using System;
using System.Collections.Generic;
using UnityEngine;
using UserInTheBox;
using Random = UnityEngine.Random;

public class TargetArea : MonoBehaviour
{
    public Target target;
    public Bomb bomb;
    public Replayer replayer;
    public AudioManager audioManager;
    private float _spawnBan;
    private float _bombSpawnBan;
    private PlayParameters _playParameters;
    private int _objectID;
    private int _numTargets;
    private int _numBombs;
    public Dictionary<int, Target> objects;
    public int numberGridPosition
    {
        get => _gridWidth*_gridHeight;
    }
    private int _gridWidth, _gridHeight;
    private Tuple<float, float>[,] _gridPositions;
    private int[,] _gridID;
    private Dictionary<int, Tuple<int, int>> _gridID2Pos;
    private List<Tuple<int, int>> _freePositions;
    private string _gridMapping;
    
    public static void Shuffle<T>(IList<T> ts) {
        // Shuffles the element order of the specified list.
        // From Smooth-P: https://forum.unity.com/threads/clever-way-to-shuffle-a-list-t-in-one-line-of-c-code.241052/
        var count = ts.Count;
        var last = count - 1;
        for (var i = 0; i < last; ++i) {
            var r = Random.Range(i, count);
            var tmp = ts[i];
            ts[i] = ts[r];
            ts[r] = tmp;
        }
    }

    public static void Shuffle(IList<Tuple<int, int>> ts, List<float> probs, int[,] gridID) {
        // Get random index position according to a list of (non-normalized) probabilities/counts
        
        var count = ts.Count;
        var last = count - 1;

        // Get total probability
        float pt = 0.0f;
        for (var j = 0; j < count; ++j) {
            var fp = ts[j];
            pt += probs[gridID[fp.Item1, fp.Item2]];
        }
        for (var i = 0; i < last; ++i) {
            var rf = Random.Range(0.0f, pt);
            float sum = 0.0f;
            int r=count>0 ? (count-1) : 0;
            for (var j = i; j < count; j++) {
                var fp = ts[j];
                sum += probs[gridID[fp.Item1, fp.Item2]];
                if (rf <= sum)
                {
                    r = j;
                    break;
                }
            }
            var tmp = ts[i];
            ts[i] = ts[r];
            ts[r] = tmp;

            // var tmp2 = probs[ts[i]];
            // probs[i] = probs[r];
            // probs[r] = tmp2;
            
            //remove probability of selected element from total probability
            pt -= probs[r];
        }
    }

    public void Awake()
    {
        // Initialise a dict for objects (targets and bombs)
        objects = new Dictionary<int, Target>();

        // Initialise grid size
        _gridHeight = 3;
        _gridWidth = 3;
        
        // Initialise grid
        _gridPositions = new Tuple<float, float>[_gridHeight, _gridWidth];
        _gridID = new int[_gridHeight, _gridWidth];
        _gridID2Pos = new Dictionary<int, Tuple<int, int>>();
        
        // Initialise list of free positions
        _freePositions = new List<Tuple<int, int>>();
    }

    public void Reset()
    {
        _numTargets = 0;
        _numBombs = 0;
        _objectID = 0;
        objects.Clear();
    }

    // public void RemoveBomb(Bomb bmb)
    // {
    //     objects.Remove(bmb.ID);
    //     _numBombs -= 1;
    //     
    //     // Add this position to the list of available positions
    //     _freePositions.Add(new Tuple<float, float>(bmb.Position.x, bmb.Position.y));
    // }

    public void RemoveTarget(Target tgt)
    {
        objects.Remove(tgt.ID);
        _numTargets -= 1;
        
        // Add this position to the list of available positions
        _freePositions.Add(new Tuple<int, int>(tgt.GridPosition.Item1, tgt.GridPosition.Item2));
    }

    public void SetPlayParameters(PlayParameters playParameters)
    {
        _playParameters = playParameters;
    }

    public void CalculateGridMapping() {

        // Get max target size
        float targetRadius = _playParameters.targetSize[1];
        float targetDiameter = 2 * targetRadius;
        
        // Create strings with grid positions and IDs, to be stored in the log file
        _gridMapping = "grid mapping";
        
        // Populate list of free positions
        _freePositions.Clear();
        for (int i = 0; i < _gridWidth; i++)
        {
            float x = -(_playParameters.targetAreaWidth / 2) + targetRadius +
                      i*(_playParameters.targetAreaWidth - targetDiameter) / ((_gridWidth-1)>0?(_gridWidth-1):1);
            for (int j = 0; j < _gridHeight; j++)
            {
                float y = (_playParameters.targetAreaHeight / 2) - targetRadius -
                          j*(_playParameters.targetAreaHeight - targetDiameter) / ((_gridHeight-1)>0?(_gridHeight-1):1);

                // Add position and ID
                _gridPositions[j, i] = new Tuple<float, float>(x, y);
                var id = j * _gridWidth + i;
                _gridID[j, i] = id;
                _gridID2Pos[id] = new Tuple<int, int>(j, i);
                
                // Calculate global position -- set targetRadius as z coordinate, as the targets are shifted that much
                // perpendicular to the plane towards positive z-axis to keep the surface of the target (semisphere) on
                // target plane
                Vector3 globalPos = transform.TransformPoint(new Vector3(x, y, targetRadius));

                // Add to grid mapping
                _gridMapping += " [" + id + " " + j + " " + i + " " + x + " " + " " + y + " " 
                                + UitBUtils.Vector3ToString(globalPos, " ") + "]";
                
                // Add to list of free positions
                _freePositions.Add(new Tuple<int, int>(j, i));
            }
        }
    }

    public string GetGridMapping()
    {
        return _gridMapping;
    }
    
    public void SetPosition(Transform headset)
    {
        transform.SetPositionAndRotation(headset.position + _playParameters.targetAreaPosition, 
            _playParameters.targetAreaRotation);
    }

    public void SetScale()
    {
        transform.Find("area").transform.localScale = new Vector3(
            _playParameters.targetAreaWidth, 
            _playParameters.targetAreaHeight,
            _playParameters.targetAreaDepth
        );
    }
    
    public bool MaybeSpawnTarget(List<float> spawnProbs_gridID = null)
    {
        // Don't sample new targets if there are enough already, or if we're replaying
        if (_numTargets >= _playParameters.maxTargets || replayer.enabled)
        {
            return false;
        } 
        
        // Sample a new target after spawn ban has passed
        if (Time.time > _spawnBan /*|| _numTargets == 0*/ )
        {

            SpawnTarget(-1, spawnProbs_gridID);
            return true;
        }

        return false;
    }

    public void SpawnTarget(int gridId=-1, List<float> spawnProbs_gridID = null)
    {
        // Instantiate a new target
        Target newTarget = Instantiate(target, transform.position, transform.rotation, transform);

        // Sample new spawn ban time
        _spawnBan = Time.time + SampleSpawnBan();

        // Sample target size
        newTarget.Size = SampleSize();

        // If grid position is not given, sample a new position
        Tuple<int, int> gridPos;
        if (gridId == -1)
        {
            gridPos = SampleGridPosition(spawnProbs_gridID);
        }
        else
        {
            gridPos = _gridID2Pos[gridId];
        }
        
        // Set position of newly spawned target
        newTarget.SetPosition(gridPos, _gridPositions[gridPos.Item1, gridPos.Item2], 
            _gridID[gridPos.Item1, gridPos.Item2]);

        // Set the position of the AudioSource to match the new target's position
        if (Globals.Instance.simulatedUser.audioModeOn) {
            audioManager.m_AudioSource.transform.position = newTarget.transform.position;
        }
        
        // Sample life span
        newTarget.LifeSpan = SampleLifeSpan();

        // Set velocity threshold for hitting
        newTarget.VelocityThreshold = _playParameters.velocityThreshold;

        // Increase number of targets
        _numTargets += 1;
            
        // Set ID and increment counter
        newTarget.ID = _objectID;
        _objectID += 1;
            
        // Add to objects
        objects.Add(newTarget.ID, newTarget);
            
        // Record target spawn
        Globals.Instance.sequenceManager.RecordSpawn(newTarget);
    }
    
    public bool spawnBomb()
    {
        // Sample a new bomb after spawn ban has passed
        if (_numBombs >= _playParameters.maxBombs)
        {
            return false;
        } 
        
        if (Time.time > _bombSpawnBan)
        {
            // Instantiate a new target
            Bomb newBomb = Instantiate(bomb, transform.position, transform.rotation, transform);

            // Sample new spawn ban time
            _bombSpawnBan = Time.time + SampleBombSpawnBan();

            // Sample target location, size, life span
            newBomb.Size = SampleSize();
            // newBomb.Position = SamplePosition(newBomb.Size);
            // newBomb.Position = SampleGridPosition(newBomb.Size);
            newBomb.LifeSpan = SampleLifeSpan();
            newBomb.VelocityThreshold = _playParameters.velocityThreshold;


            _numBombs += 1;
            
            // Set ID and increment counter
            newBomb.ID = _objectID;
            _objectID += 1;
            
            // Add to objects
            // objects.Add(newBomb.ID, new Tuple<Vector3, float>(newBomb.Position, newBomb.Size));

            // if (_logger.enabled)
            // {
            //     // Log the event
            //     _logger.PushWithTimestamp("events", "spawn_bomb, " + newBomb.ID + ", " 
            //                                         + newBomb.PositionToString());
            // }

            return true;
        }
        
        return false;
    }

    private Vector3 SamplePosition(float targetSize)
    {
        // Go through all existing targets, sample position until a suitable one is found (not overlapping other
        // targets). If a suitable position isn't found in 20 attempts, just use whatever position is latest
        float x = 0, y = 0, z = 0;
        Vector3 pos = new Vector3(x, y, z);
        int idx = 0;
        for (; idx < 10; idx++)
        {
            x = Random.Range(-_playParameters.targetAreaWidth/2, _playParameters.targetAreaWidth/2);
            y = Random.Range(-_playParameters.targetAreaHeight/2, _playParameters.targetAreaHeight/2);
            z = Random.Range(-_playParameters.targetAreaDepth/2, _playParameters.targetAreaDepth/2) + targetSize;
            pos = new Vector3(x, y, z);
            var good = true;
            
            foreach (var objectInfo in objects.Values)
            {
                // If the suggested position overlaps with the position of another object, break and sample a new
                // position.
                if (Vector3.Distance(objectInfo.transform.position, pos) < (objectInfo.Size+targetSize))
                {
                    good = false;
                    break;
                }
            }

            // If the found position was good, break the loop
            if (good)
            {
                break;
            }
            
        }
        
        return pos;
    }

    private Tuple<int, int> SampleGridPosition(List<float> spawnProbs_gridID = null)
    {
        // Note! Works currently only on 2D grids
        
        // If there are no more free positions, throw an exception
        if (_freePositions.Count == 0)
        {
            throw new IndexOutOfRangeException("Out of free positions, too many targets!");
        }
        
        // Shuffle the list and return first element
        if (spawnProbs_gridID is object)
        {
            Shuffle(_freePositions, spawnProbs_gridID, _gridID);
        }
        else
        {
            Shuffle(_freePositions);
        }
        Tuple<int, int> gridPos = _freePositions[0];
        _freePositions.RemoveAt(0);
        
        // z needs to be targetSize so the target is correctly positioned on the grid
        // return new Vector3(pos.Item1, pos.Item2, targetSize);
        return gridPos;
    }
    
    private float SampleSize()
    {
        return Random.Range(_playParameters.targetSize[0], _playParameters.targetSize[1]);
    }

    private float SampleLifeSpan()
    {
        return Random.Range(_playParameters.targetLifeSpan[0], _playParameters.targetLifeSpan[1]);
    }

    private float SampleSpawnBan()
    {
        return Random.Range(_playParameters.targetSpawnBan[0], _playParameters.targetSpawnBan[1]);
    }

    private float SampleBombSpawnBan()
    {
        return Random.Range(_playParameters.bombSpawnBan[0], _playParameters.bombSpawnBan[1]);
    }
}
