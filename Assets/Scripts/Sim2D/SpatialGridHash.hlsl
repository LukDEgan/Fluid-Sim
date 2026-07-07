static const int2 offsets[9] =
{
    int2(-1,1),
    int2(0,1),
    int2(1,1),
    int2(-1,0),
    int2(0,0),
    int2(1,0),
    int2(-1,-1),
    int2(0,-1),
    int2(1,-1),
};

static const uint hashK1 = 15823;
static const uint hashK2 = 9737333;

int2 PositionToCellCord(float2 position, float cellSize){
    return (int2)floor(position/cellSize);
}

uint HashCell(int2 cell){
    cell = (uint)cell;
    uint a = cell.x * hashK1;
    uint b = cell.y * hashK2;
    return (a+b);
}

uint GetKeyFromHash(uint hash, uint tableSize){
    return hash % tableSize;
}
