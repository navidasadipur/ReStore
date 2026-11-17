import { createAsyncThunk, createSlice, isAnyOf } from "@reduxjs/toolkit";
import { User } from "../../app/models/user";
import { FieldValues } from "react-hook-form";
import agent from "../../app/api/agent";
import { clearBasket, setBasket } from "../basket/basketSlice";
import { ApiErrorData, toApiErrorData } from "../../app/api/contracts";
import { readStoredUser, withRoles } from "../../app/util/userSession";

interface AccountState {
    user: User | null;
}

const initialState: AccountState = {
    user: null
}

export const signInUser = createAsyncThunk<User, FieldValues, {rejectValue: ApiErrorData}>(
    'account/signInUser',
    async (data, ThunkAPI) => {
        try {
            const userDto = await agent.Account.login(data);
            const {basket, ...user} = userDto;
            ThunkAPI.dispatch(setBasket(basket));
            localStorage.setItem('user', JSON.stringify(user));
            return user;
        } catch (error: any) {
            return ThunkAPI.rejectWithValue(toApiErrorData(error));
        }
    }
)

export const fetchCurrentUser = createAsyncThunk<User, void, {rejectValue: ApiErrorData}>(
    'account/fetchCurrentUser',
    async (_, ThunkAPI) => {
        ThunkAPI.dispatch(setUser(readStoredUser()));
        try {
            const userDto = await agent.Account.currentUser();
            const {basket, ...user} = userDto;
            ThunkAPI.dispatch(setBasket(basket));
            localStorage.setItem('user', JSON.stringify(user));
            return user;
        } catch (error: any) {
            ThunkAPI.dispatch(clearBasket());
            return ThunkAPI.rejectWithValue(toApiErrorData(error));
        }
    },
    {
        condition: () => {
            return readStoredUser() !== null;
        }
    }
)

export const accountSlice = createSlice({
    name: 'account',
    initialState,
    reducers: {
        signOut: (state) => {
            state.user = null;
            localStorage.removeItem('user');
        },
        setUser: (state, action) => {
            state.user = action.payload ? withRoles(action.payload) : null;
        }
    },
    extraReducers: (builder => {
        builder.addCase(fetchCurrentUser.rejected, (state) => {
            state.user = null;
            localStorage.removeItem('user');
        });
        builder.addMatcher(isAnyOf(signInUser.fulfilled, fetchCurrentUser.fulfilled), (state, action) => {
            state.user = withRoles(action.payload);
        });
    })
})

export const {signOut, setUser} = accountSlice.actions;
