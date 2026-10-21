local holdId = redis.call('GET', KEYS[1])
local holdData = redis.call('GET', KEYS[2])
local slotKeyStartIndex = 3

if holdId ~= ARGV[1] then 
    return 2
end

if holdData ~= ARGV[2] then
    return 3
end

for i = slotKeyStartIndex, #KEYS do
    local slotHoldId = redis.call('GET', KEYS[i])
    
    if slotHoldId ~= ARGV[1] then
        return 6
    end
end

return 1

