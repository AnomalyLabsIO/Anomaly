Messages.On("GetState", function(message)
    if message.Command == "show" or message.Command == "clear" then
        assert(Output:ShowText("remote owner B remains independent", 10))
    elseif message.Command == "expire" then
        assert(Output:ShowText("remote owner B expires", 1))
    end
end)
Output:Log("Remote smoke independent owner fully loaded.")
