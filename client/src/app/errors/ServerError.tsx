import { Button, Container, Divider, Paper, Typography } from "@mui/material";
import { useLocation, useNavigate } from "react-router-dom";

export default function ServerError(){
    const navigate = useNavigate();
    const {state} = useLocation();
    return (
        <Container component={Paper}>
            {state?.error ? (
                <>
                <Typography variant='h3' color='error' gutterBottom >{state.error.title}</Typography>
                <Divider />
                <Typography>{state.error.detail || 'Internal server error'}</Typography>
                </>
            ) : (
                <Typography variant='h5' color='error' gutterBottom >Server error</Typography>
            )}
            <Button onClick={() => navigate('/catalog')}>Go back to the store</Button>
        </Container>
    )
}