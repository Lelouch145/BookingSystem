local storeHoldId = redis.call('GET', KEYS[1])
local holdKey = redis.call('GET', KEYS[2])

if redis.call('EXISTS', KEYS[#KEYS]) == 1 then
    return {9, 0}
end
-- Controll if the user has same holdId
if storeHoldId ~= ARGV[1] then
    return {2, 0}
end
-- Controll if the old hold infromation is still the same
if holdKey ~= ARGV[4] then
    return {3, 0}
end

local newSlotCount = tonumber(ARGV[3])
local newSlotStartIndex = 3
local newSlotEndIndex = 2 + newSlotCount
 -- Check if any new slot is reserved by another hold
for i = newSlotStartIndex, newSlotEndIndex do
    local slots = redis.call('GET', KEYS[i])
    if slots ~= false and slots ~= ARGV[1] then
        return {4, 0}
    end
end

local expiresAtMs = redis.call('PEXPIRETIME', KEYS[2])
local oldSlotStartIndex = 3 + newSlotCount
 -- Check if the hold has a valid expiration time
if expiresAtMs <= 0 then
    return {5, 0}
end

-- Check if all old slots still belong to the existing hold
for i = oldSlotStartIndex, #KEYS - 1 do
    local oldKeys = redis.call('GET', KEYS[i])
    if oldKeys ~= ARGV[1] then
        return {6, 0}
    end
end



for i = oldSlotStartIndex, #KEYS do
    local existsInNew = false

    for k = newSlotStartIndex, newSlotEndIndex do
        if(KEYS[i] == KEYS[k]) then
            existsInNew = true
            break
        end
    end
    if existsInNew == false then
        redis.call('DEL', KEYS[i])
    end
end

for i = newSlotStartIndex, newSlotEndIndex do
    redis.call('SET', KEYS[i], ARGV[1], 'PXAT', expiresAtMs)
end

redis.call('SET', KEYS[2], ARGV[2], 'PXAT', expiresAtMs)
return {1, redis.call('EXPIRETIME', KEYS[2])}
